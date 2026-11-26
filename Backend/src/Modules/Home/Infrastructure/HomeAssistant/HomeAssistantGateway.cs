using Backend.Modules.Home.Domain;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using static Backend.Modules.Home.Infrastructure.HomeAssistant.HomeAssistantProtocol;

namespace Backend.Modules.Home.Infrastructure.HomeAssistant;

/// <summary>
/// The one Home Assistant adapter of the Home module (AD-18). Runs in the background and keeps one WebSocket
/// connection: authenticates, subscribes to state changes of the motion sensor and the desk (switch and power), and
/// publishes them through the mediator as <see cref="MotionDetectedDomainEvent"/> (only changes to <c>on</c>),
/// <see cref="DeskSwitched"/>, <see cref="DeskHasBeenSwitchedOffDomainEvent"/> (additionally, for changes to
/// <c>off</c>) and <see cref="DeskPowerChanged"/>.
/// Home Assistant delivers events at most once and without replay: motion during a lost connection is lost,
/// and after a reconnect only new motion is reported.
/// </summary>
internal sealed class HomeAssistantGateway : BackgroundService, IHomeAssistantGateway
{
    /// <summary>Name of the client for the REST API, configured in <see cref="HomeModule.AddHome"/>.</summary>
    public const string HttpClientName = "HomeAssistant";

    private readonly HomeAssistantOptions _options;
    private readonly IServiceScopeFactory _scopes;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TimeProvider _time;
    private readonly ILogger<HomeAssistantGateway> _logger;

    private volatile GatewayStatus _status;

    // Only touched by the connection loop; kept across reconnects.
    private readonly Dictionary<string, DateTimeOffset> _lastPublished = [];

    public HomeAssistantGateway(
        IOptions<HomeAssistantOptions> options,
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopes,
        TimeProvider time,
        ILogger<HomeAssistantGateway> logger)
    {
        _options = options.Value;
        _scopes = scopes;
        _httpClientFactory = httpClientFactory;
        _time = time;
        _logger = logger;
        _status = new GatewayStatus(
            _options.IsConfigured ? GatewayConnectionState.Disconnected : GatewayConnectionState.NotConfigured,
            time.GetUtcNow());
    }

    public GatewayStatus Status => _status;

    public Task TurnAllSwitchesOnAsync(CancellationToken cancellationToken) => CallSwitchServiceAsync("turn_on", cancellationToken);

    public Task TurnAllSwitchesOffAsync(CancellationToken cancellationToken) => CallSwitchServiceAsync("turn_off", cancellationToken);

    /// <summary>A REST service call, independent of the WebSocket connection. It names a target state, never a toggle (AD-9).</summary>
    private async Task CallSwitchServiceAsync(string service, CancellationToken cancellationToken)
    {
        var entityIds = _options.AllSwitchEntityIds;
        if (!_options.IsConfigured || entityIds.Count == 0)
        {
            _logger.LogWarning("Not calling switch.{Service}: the gateway or the switches are not configured.", service);
            return;
        }

        using var http = _httpClientFactory.CreateClient(HttpClientName);
        using var response = await http.PostAsJsonAsync($"api/services/switch/{service}", new { entity_id = entityIds }, cancellationToken);
        if(response.IsSuccessStatusCode)
        {
            _logger.LogInformation("Called switch.{Service} for {EntityIds}.", service, string.Join(",", entityIds));
        }
        else
        {
            _logger.LogError("Switches: {EntityIds} could not be switched {Service}: {Reason}", string.Join(",",entityIds), service,await response.Content.ReadAsStringAsync(cancellationToken));
        }
        
    }

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

        // Read after subscribing, so no change falls between the two; events of the subscription wait in the socket.
        await PublishCurrentStatesAsync(cancellationToken);

        while (true)
        {
            using var stateUpdate = await ReadAsync(socket, cancellationToken) ?? throw new IOException("Connection closed.");
            if (TryParseTrigger(stateUpdate.RootElement, out var change) && IsNewerThanPublished(change!))
            {
                await PublishDomainEventAsync(change!, cancellationToken);
            }
        }
    }

    /// <summary>
    /// After every (re)connect the current state of each entity is published, even if unchanged: changes during a
    /// restart or a lost connection are not replayed by Home Assistant.
    /// </summary>
    private async Task PublishCurrentStatesAsync(CancellationToken cancellationToken)
    {
        using var http = _httpClientFactory.CreateClient(HttpClientName);

        foreach (var entityId in _options.SubscribedEntityIds)
        {
            using var response = await http.GetAsync($"api/states/{Uri.EscapeDataString(entityId)}", cancellationToken);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Home Assistant does not know the entity {EntityId}; check the configuration.", entityId);
                continue;
            }

            response.EnsureSuccessStatusCode();
            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var state = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            await PublishDomainEventAsync(ParseState(state.RootElement), cancellationToken);
        }
    }

    /// <summary>A trigger event for a change already published from the state read (or older) is dropped.</summary>
    private bool IsNewerThanPublished(StateChange change) =>
        !_lastPublished.TryGetValue(change.EntityId, out var last) || change.ChangedAt > last;

    private async Task SubscribeTrigger(ClientWebSocket socket, int subscriptionId, CancellationToken cancellationToken)
    {
        await SendAsync(socket, SubscribeTriggerStateChanges(subscriptionId, _options.SubscribedEntityIds), cancellationToken);

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
                throw new InvalidOperationException("Home Assistant rejected the subscription to the motion sensor and the desk.");
            }
            SetStatus(GatewayConnectionState.Connected);
            return;
        }
    }

    /// <summary>One DI scope per event (AD-15). A failing handler must not take down the connection.</summary>
    private Task PublishDomainEventAsync(StateChange change, CancellationToken cancellationToken)
    {
        if (!_lastPublished.TryGetValue(change.EntityId, out var last) || change.ChangedAt > last)
        {
            _lastPublished[change.EntityId] = change.ChangedAt;
        }

        if (change.EntityId == _options.MotionSensorEntityId)
        {
            return IsOnState(change)
                ? PublishAsync(new MotionDetectedDomainEvent(change.ChangedAt), change, cancellationToken)
                : Task.CompletedTask;
        }

        if (change.EntityId == _options.DeskSwitchEntityId)
        {
            return IsOnState(change) || IsOffState(change)
                ? PublishDeskSwitchAsync(change, cancellationToken)
                : Task.CompletedTask;
        }

        if (change.EntityId == _options.DeskPowerEntityId)
        {
            return PublishAsync(new DeskPowerChanged(Number(change), change.ChangedAt), change, cancellationToken);
        }

        return Task.CompletedTask;
    }

    private async Task PublishDeskSwitchAsync(StateChange change, CancellationToken cancellationToken)
    {
        await PublishAsync(new DeskSwitched(IsOnState(change), change.ChangedAt), change, cancellationToken);

        if (IsOffState(change))
        {
            await PublishAsync(new DeskHasBeenSwitchedOffDomainEvent(change.ChangedAt), change, cancellationToken);
        }
    }

    private async Task PublishAsync<TEvent>(TEvent domainEvent, StateChange change, CancellationToken cancellationToken)
        where TEvent : INotification
    {
        try
        {
            await using var scope = _scopes.CreateAsyncScope();
            var publisher = scope.ServiceProvider.GetRequiredService<IPublisher>();
            await publisher.Publish(domainEvent, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Handling {Event} from {EntityId} at {ChangedAt} failed.", typeof(TEvent).Name, change.EntityId, change.ChangedAt);
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
