using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Backend.Tests.Home;

/// <summary>
/// A small in-process Home Assistant: speaks just enough of the WebSocket protocol (auth_required, auth, subscribe_trigger,
/// events) over a real socket, and can misbehave (reject the token, drop the connection, refuse connections).
/// </summary>
public sealed class FakeHomeAssistantServer : IAsyncDisposable
{
    public const string ValidToken = "test-token";

    private readonly List<FakeConnection> _connections = [];
    private WebApplication? _app;
    private int _port;
    private int _attempts;

    public string Token { get; set; } = ValidToken;

    public bool RefuseConnections { get; set; }

    public IReadOnlyList<FakeConnection> Connections
    {
        get { lock (_connections) { return [.. _connections]; } }
    }

    public int ConnectionAttempts => _attempts;

    public string Url => $"http://127.0.0.1:{_port}";

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls(_port == 0 ? "http://127.0.0.1:0" : $"http://127.0.0.1:{_port}");
        _app = builder.Build();
        _app.UseWebSockets();
        _app.Map("/api/websocket", Handle);
        await _app.StartAsync();

        _port = new Uri(_app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
    }

    public async Task StopAsync()
    {
        if (_app is not null)
        {
            await _app.StopAsync(TimeSpan.Zero);
            await _app.DisposeAsync();
            _app = null;
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    private async Task Handle(HttpContext context)
    {
        Interlocked.Increment(ref _attempts);

        if (RefuseConnections || !context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = new FakeConnection(socket);
        lock (_connections) { _connections.Add(connection); }

        await connection.SendAsync("""{"type":"auth_required","ha_version":"2026.11.0"}""");

        var auth = await connection.ReceiveAsync();
        if (auth is null)
        {
            return;
        }

        using (auth)
        {
            if (auth.RootElement.GetProperty("access_token").GetString() != Token)
            {
                await connection.SendAsync("""{"type":"auth_invalid","message":"Invalid access token"}""");
                await connection.CloseAsync();
                return;
            }
        }

        await connection.SendAsync("""{"type":"auth_ok","ha_version":"2026.11.0"}""");
        connection.Authenticated = true;

        while (true)
        {
            using var message = await connection.ReceiveAsync();
            if (message is null)
            {
                return;
            }

            var root = message.RootElement;
            var id = root.GetProperty("id").GetInt32();
            var type = root.GetProperty("type").GetString()!;
            connection.Received.Add(type);

            if (type == "subscribe_trigger")
            {
                var trigger = root.GetProperty("trigger");
                connection.TriggerPlatform = trigger.GetProperty("platform").GetString();
                var entityIds = trigger.GetProperty("entity_id");
                connection.TriggerEntityIds = entityIds.ValueKind == JsonValueKind.Array
                    ? [.. entityIds.EnumerateArray().Select(e => e.GetString()!)]
                    : [entityIds.GetString()!];
                connection.TriggerHasTo = trigger.TryGetProperty("to", out var to);
                connection.TriggerTo = connection.TriggerHasTo && to.ValueKind == JsonValueKind.String ? to.GetString() : null;
                connection.SubscriptionId = id;
                await connection.SendAsync($$"""{"id":{{id}},"type":"result","success":true,"result":null}""");
            }
        }
    }

    /// <summary>A <c>state</c> JSON object as a trigger event carries it in <c>from_state</c> and <c>to_state</c>.</summary>
    public static string State(string entityId, string state, DateTimeOffset lastChanged, string attributes = "{}") =>
        $$"""{"entity_id":"{{entityId}}","state":"{{state}}","attributes":{{attributes}},"last_changed":"{{lastChanged:O}}","last_updated":"{{lastChanged:O}}"}""";

    /// <summary>An event of a <c>subscribe_trigger</c> state trigger, in the shape Home Assistant sends it.</summary>
    public static string TriggerEvent(int subscriptionId, string entityId, string fromState, string toState, DateTimeOffset changedAt) =>
        "{\"id\":" + subscriptionId
        + ",\"type\":\"event\",\"event\":{\"variables\":{\"trigger\":{\"id\":\"0\",\"idx\":\"0\",\"alias\":null,\"platform\":\"state\""
        + ",\"entity_id\":\"" + entityId + "\""
        + ",\"from_state\":" + State(entityId, fromState, DateTimeOffset.UnixEpoch)
        + ",\"to_state\":" + State(entityId, toState, changedAt)
        + ",\"for\":null,\"attribute\":null,\"description\":\"state of " + entityId + "\"}}"
        + ",\"context\":{\"id\":\"01M827DN605R57EGV75C7AM9BA\",\"parent_id\":null,\"user_id\":null}}}";

    public sealed class FakeConnection(WebSocket socket)
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public bool Authenticated { get; set; }

        public string? TriggerPlatform { get; set; }

        public string[] TriggerEntityIds { get; set; } = [];

        /// <summary>Whether the state trigger has a <c>to</c> at all; <c>to: null</c> means state changes only, no attribute changes.</summary>
        public bool TriggerHasTo { get; set; }

        /// <summary>The <c>to</c> of the state trigger the gateway subscribed with.</summary>
        public string? TriggerTo { get; set; }

        public int SubscriptionId { get; set; } = 1;

        /// <summary>The types of the requests received after authentication, in order.</summary>
        public List<string> Received { get; } = [];

        public async Task SendAsync(string json)
        {
            await _sendLock.WaitAsync();
            try
            {
                await socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public Task SendTriggerEventAsync(string entityId, string fromState, string toState, DateTimeOffset changedAt) =>
            SendAsync(TriggerEvent(SubscriptionId, entityId, fromState, toState, changedAt));

        /// <summary>Drops the connection abruptly, like a restart of Home Assistant or a network failure.</summary>
        public void Drop() => socket.Abort();

        public async Task CloseAsync()
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch (WebSocketException)
                {
                }
            }
        }

        public async Task<JsonDocument?> ReceiveAsync()
        {
            var buffer = new byte[16 * 1024];
            using var stream = new MemoryStream();

            while (true)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await socket.ReceiveAsync(buffer, CancellationToken.None);
                }
                catch (WebSocketException)
                {
                    return null;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    try
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    }
                    catch (WebSocketException)
                    {
                    }

                    return null;
                }

                stream.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                {
                    return JsonDocument.Parse(stream.ToArray());
                }
            }
        }
    }
}
