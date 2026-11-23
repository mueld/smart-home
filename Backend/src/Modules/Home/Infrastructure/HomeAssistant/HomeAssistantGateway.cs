using Backend.Modules.Home.Domain;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using static Backend.Modules.Home.Infrastructure.HomeAssistant.HomeAssistantProtocol;

namespace Backend.Modules.Home.Infrastructure.HomeAssistant;

/// <summary>
/// The one Home Assistant adapter of the Home module (AD-18). Runs in the background and keeps one WebSocket
/// connection: authenticates, subscribes to the motion sensor changing to <c>on</c>, and publishes a
/// <see cref="MotionDetected"/> domain event through the mediator for every such event.
/// Home Assistant delivers events at most once and without replay: motion during a lost connection is lost,
/// and after a reconnect only new motion is reported.
/// </summary>
internal sealed class HomeAssistantGateway : BackgroundService, IHomeAssistantGateway
{
    private readonly HomeAssistantOptions _options;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeProvider _time;
    private readonly ILogger<HomeAssistantGateway> _logger;

    private volatile GatewayStatus _status;

    public HomeAssistantGateway(
        IOptions<HomeAssistantOptions> options,
        IServiceScopeFactory scopes,
        TimeProvider time,
        ILogger<HomeAssistantGateway> logger)
    {
        _options = options.Value;
        _scopes = scopes;
        _time = time;
        _logger = logger;
        _status = new GatewayStatus(
            _options.IsConfigured ? GatewayConnectionState.Disconnected : GatewayConnectionState.NotConfigured,
            time.GetUtcNow());
    }

    public GatewayStatus Status => _status;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.IsConfigured)
        {
            _logger.LogError(
                "Home Assistant gateway is not configured: set HomeAssistant:Url, HomeAssistant:Token and HomeAssistant:MotionSensorEntityId. The gateway stays off.");
            return;
        }

        var uri = BuildWebSocketUri(_options.Url);
        var failures = 0;
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var established = false;
            attempt++;
            SetStatus(GatewayConnectionState.Connecting);

            try
            {
                await RunConnectionAsync(uri, () => established = true, stoppingToken);
                _logger.LogWarning("Home Assistant closed the connection.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (AuthenticationFailedException)
            {
                SetStatus(GatewayConnectionState.AuthenticationFailed);
                _logger.LogError("Home Assistant rejected the access token (auth_invalid). The gateway stops connecting until the backend restarts.");
                return;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Home Assistant connection failed (attempt {Attempt}).", attempt);
            }

            SetStatus(GatewayConnectionState.Disconnected);

            failures = established ? 0 : failures + 1;
            var delay = NextDelay(established ? 0 : failures - 1);
            _logger.LogInformation("Reconnecting to Home Assistant in {Delay} (attempt {Attempt} ended).", delay, attempt);

            try
            {
                await Task.Delay(delay, _time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private TimeSpan NextDelay(int doublings)
    {
        var delay = _options.InitialBackoff;
        for (var i = 0; i < doublings && delay < _options.MaxBackoff; i++)
        {
            delay *= 2;
        }

        return delay < _options.MaxBackoff ? delay : _options.MaxBackoff;
    }

    private async Task RunConnectionAsync(Uri uri, Action established, CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(20);

        using (var connectTimeout = new CancellationTokenSource(_options.ConnectTimeout, _time))
        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectTimeout.Token))
        {
            await socket.ConnectAsync(uri, linked.Token);
        }

        using (var first = await ReadAsync(socket, cancellationToken) ?? throw new IOException("Connection closed before auth_required."))
        {
            if (!IsAuthRequest(first.RootElement))
            {
                throw new InvalidOperationException("Home Assistant did not ask for authentication.");
            }
        }

        await SendAsync(socket, Auth(_options.Token), cancellationToken);

        using (var reply = await ReadAsync(socket, cancellationToken) ?? throw new IOException("Connection closed during authentication."))
        {
            ValidateAuthResponseOrThrow(reply.RootElement);
        }

        // Ids are ascending and unique per connection and start again after a reconnect.
        const int subscriptionId = 1;
        await SubscribeTrigger(socket, subscriptionId, cancellationToken);

        while(true)
        {
            using var stateUpdate = await ReadAsync(socket, cancellationToken) ?? throw new IOException("Connection closed.");
            if (TryParseTrigger(stateUpdate.RootElement, out var change))
            {
                await PublishDomainEventAsync(change!, cancellationToken);
            }
        }        
    }

    private async Task SubscribeTrigger(ClientWebSocket socket, int subscriptionId, CancellationToken cancellationToken)
    {
        await SendAsync(socket, SubscribeTriggerStateOn(subscriptionId, _options.MotionSensorEntityId), cancellationToken);

        while (true)
        {
            using var message = await ReadAsync(socket, cancellationToken)
                ?? throw new IOException("Connection closed before the subscription was confirmed.");

            var root = message.RootElement;

            var subscriptionConfirmed = SubscriptionConfirmed(subscriptionId, root);

            if (subscriptionConfirmed is null)
            {
                continue;
            }

            if (subscriptionConfirmed is false)
            {
                throw new InvalidOperationException("Home Assistant rejected the subscription to the motion sensor.");
            }
            SetStatus(GatewayConnectionState.Connected);
            return;
        }
    }

    /// <summary>One DI scope per event (AD-15). A failing handler must not take down the connection.</summary>
    private async Task PublishDomainEventAsync(StateChange change,  CancellationToken cancellationToken)
    {
        if (!IsOnState(change) || change.EntityId != _options.MotionSensorEntityId)
        {
            return;
        }

        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
            await publisher.Publish(new MotionDetected(change.EntityId, change.ChangedAt), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Handling MotionDetected from {EntityId} at {DetectedAt} failed.", change.EntityId, change.ChangedAt);
        }
    }

    private void SetStatus(GatewayConnectionState state)
    {
        if (_status.State == state)
        {
            return;
        }

        _status = new GatewayStatus(state, _time.GetUtcNow());
        _logger.LogInformation("Home Assistant gateway is now {State}.", state);
    }

    private static Uri BuildWebSocketUri(string url)
    {
        var builder = new UriBuilder(url);
        builder.Scheme = builder.Scheme switch
        {
            "http" => "ws",
            "https" => "wss",
            var scheme => scheme,
        };
        builder.Path = "/api/websocket";
        return builder.Uri;
    }

    private static async Task SendAsync(WebSocket socket, byte[] payload, CancellationToken cancellationToken) =>
        await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

    /// <summary>Reads one complete text message, or null when the peer closed the connection.</summary>
    private static async Task<JsonDocument?> ReadAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>(4096);

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer.GetMemory(4096), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            buffer.Advance(result.Count);

            if (result.EndOfMessage)
            {
                return JsonDocument.Parse(buffer.WrittenMemory);
            }
        }
    }


}
