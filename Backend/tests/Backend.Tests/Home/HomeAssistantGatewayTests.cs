using Backend.Modules.Home;
using Backend.Modules.Home.Domain;
using Backend.Modules.Home.Infrastructure.HomeAssistant;
using Mediator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Backend.Tests.Home;

/// <summary>Collects the <see cref="MotionDetected"/> domain events that reach the mediator; can be told to fail.</summary>
public sealed class MotionRecorder
{
    private readonly List<MotionDetected> _events = [];

    public int FailuresLeft { get; set; }

    public MotionDetected[] Events { get { lock (_events) { return [.. _events]; } } }

    public void Add(MotionDetected motion)
    {
        if (FailuresLeft > 0)
        {
            FailuresLeft--;
            throw new InvalidOperationException("handler failed");
        }

        lock (_events) { _events.Add(motion); }
    }
}

public sealed class RecordMotionDetectedHandler(MotionRecorder recorder) : INotificationHandler<MotionDetected>
{
    public ValueTask Handle(MotionDetected notification, CancellationToken cancellationToken)
    {
        recorder.Add(notification);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Collects the desk domain events (<see cref="DeskSwitched"/>, <see cref="DeskPowerChanged"/>) in arrival order.</summary>
public sealed class DeskRecorder
{
    private readonly List<object> _events = [];

    public object[] Events { get { lock (_events) { return [.. _events]; } } }

    public void Add(object domainEvent)
    {
        lock (_events) { _events.Add(domainEvent); }
    }
}

public sealed class RecordDeskEventsHandler(DeskRecorder recorder)
    : INotificationHandler<DeskSwitched>, INotificationHandler<DeskHasBeenSwitchedOffDomainEvent>,
        INotificationHandler<DeskPowerChanged>
{
    public ValueTask Handle(DeskHasBeenSwitchedOffDomainEvent notification, CancellationToken cancellationToken)
    {
        recorder.Add(notification);
        return ValueTask.CompletedTask;
    }

    public ValueTask Handle(DeskSwitched notification, CancellationToken cancellationToken)
    {
        recorder.Add(notification);
        return ValueTask.CompletedTask;
    }

    public ValueTask Handle(DeskPowerChanged notification, CancellationToken cancellationToken)
    {
        recorder.Add(notification);
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Tests the Home Assistant adapter at its seam: the domain events that reach the mediator, the status it reports
/// and what it sends over a real WebSocket to an in-process fake Home Assistant. Time is controlled.
/// </summary>
public sealed class HomeAssistantGatewayTests : IAsyncLifetime
{
    private const string Motion = "binary_sensor.living_room_eingang_occupancy";
    private const string DeskSwitch = "switch.living_room_schreibtisch";
    private const string DeskPower = "sensor.living_room_schreibtisch_power";

    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    private readonly FakeHomeAssistantServer _server = new();
    private readonly FakeTimeProvider _time = new(T0);
    private readonly MotionRecorder _recorder = new();
    private readonly DeskRecorder _deskRecorder = new();
    private ServiceProvider? _services;
    private IHomeAssistantGateway _gateway = null!;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
        {
            foreach (var hosted in _services.GetServices<IHostedService>())
            {
                await hosted.StopAsync(CancellationToken.None);
            }

            await _services.DisposeAsync();
        }

        await _server.DisposeAsync();
    }

    private async Task StartGatewayAsync(string? token = FakeHomeAssistantServer.ValidToken, string? url = null, string maxBackoff = "00:00:30", string? motionSensor = Motion, string? deskSwitch = DeskSwitch, string? deskPower = DeskPower)
    {
        if (url is null)
        {
            await _server.StartAsync();
        }

        var settings = new Dictionary<string, string?>
        {
            ["HomeAssistant:Url"] = url ?? _server.Url,
            ["HomeAssistant:Token"] = token,
            ["HomeAssistant:MotionSensorEntityId"] = motionSensor,
            ["HomeAssistant:DeskSwitchEntityId"] = deskSwitch,
            ["HomeAssistant:DeskPowerEntityId"] = deskPower,
            ["HomeAssistant:MaxBackoff"] = maxBackoff,
        };

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Critical));
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(_recorder);
        services.AddSingleton(_deskRecorder);
        services.AddMediator(options => options.ServiceLifetime = ServiceLifetime.Scoped);
        services.AddHome(configuration);
        _services = services.BuildServiceProvider();

        _gateway = _services.GetRequiredService<IHomeAssistantGateway>();
        foreach (var hosted in _services.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }
    }

    private static async Task Eventually(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for: {what}");
            }

            await Task.Delay(10);
        }
    }

    private Task WaitConnected() => Eventually(() => _gateway.Status.State == GatewayConnectionState.Connected, "gateway connected");

    private static async Task Quiet() => await Task.Delay(150);

    [Fact]
    public async Task It_authenticates_and_subscribes_to_state_changes_of_the_motion_sensor_and_the_desk()
    {
        await StartGatewayAsync();
        await WaitConnected();

        var connection = Assert.Single(_server.Connections);
        Assert.True(connection.Authenticated);
        Assert.Equal("state", connection.TriggerPlatform);
        Assert.Equal([Motion, DeskSwitch, DeskPower], connection.TriggerEntityIds);
        Assert.True(connection.TriggerHasTo);
        Assert.Null(connection.TriggerTo); // to: null = state changes only, no attribute changes
        Assert.Equal(["subscribe_trigger"], connection.Received);
    }

    [Fact]
    public async Task Without_desk_entities_it_subscribes_to_the_motion_sensor_only()
    {
        await StartGatewayAsync(deskSwitch: null, deskPower: null);
        await WaitConnected();

        Assert.Equal([Motion], _server.Connections[0].TriggerEntityIds);
    }

    [Fact]
    public async Task A_desk_power_change_is_published_with_watts_and_the_time_Home_Assistant_gives()
    {
        await StartGatewayAsync();
        await WaitConnected();
        var connection = _server.Connections[0];

        await connection.SendTriggerEventAsync(DeskPower, "3.1", "83.4", T0.AddMinutes(1));
        await connection.SendTriggerEventAsync(DeskPower, "83.4", "unavailable", T0.AddMinutes(2));
        await Eventually(() => _deskRecorder.Events.Length == 2, "two power events");

        Assert.Collection(
            _deskRecorder.Events.Cast<DeskPowerChanged>(),
            e => { Assert.Equal(83.4, e.Watts); Assert.Equal(T0.AddMinutes(1), e.ChangedAt); },
            e => { Assert.Null(e.Watts); Assert.Equal(T0.AddMinutes(2), e.ChangedAt); });
    }

    private void SetStates(DateTimeOffset at, string motion = "on", string deskSwitch = "on", string deskPower = "83.4")
    {
        _server.States[Motion] = FakeHomeAssistantServer.State(Motion, motion, at);
        _server.States[DeskSwitch] = FakeHomeAssistantServer.State(DeskSwitch, deskSwitch, at);
        _server.States[DeskPower] = FakeHomeAssistantServer.State(DeskPower, deskPower, at);
    }

    [Fact]
    public async Task After_subscribing_it_reads_the_current_states_and_publishes_them()
    {
        await _server.StartAsync();
        SetStates(T0.AddMinutes(-3));

        await StartGatewayAsync(url: _server.Url);
        await Eventually(() => _deskRecorder.Events.Length == 2 && _recorder.Events.Length == 1, "the initial states");

        Assert.Equal(["subscribe_trigger", $"GET {Motion}", $"GET {DeskSwitch}", $"GET {DeskPower}"], _server.Log);
        Assert.Equal(T0.AddMinutes(-3), Assert.Single(_recorder.Events).DetectedAt);
        var events = _deskRecorder.Events;
        Assert.Contains(events, e => e is DeskSwitched { IsOn: true });
        Assert.Contains(events, e => e is DeskPowerChanged { Watts: 83.4 });
    }

    [Fact]
    public async Task A_motion_sensor_that_is_off_at_the_start_is_not_published()
    {
        await _server.StartAsync();
        SetStates(T0, motion: "off");

        await StartGatewayAsync(url: _server.Url);
        await Eventually(() => _deskRecorder.Events.Length == 2, "the desk states");
        await Quiet();

        Assert.Empty(_recorder.Events);
    }

    [Fact]
    public async Task After_a_reconnect_the_states_are_read_and_published_again()
    {
        await _server.StartAsync();
        SetStates(T0, motion: "off");
        await StartGatewayAsync(url: _server.Url);
        await Eventually(() => _deskRecorder.Events.Length == 2, "the first states");

        _server.Connections[0].Drop();
        await Eventually(() => _gateway.Status.State == GatewayConnectionState.Disconnected, "disconnected");
        _time.Advance(TimeSpan.FromSeconds(1));

        await Eventually(() => _deskRecorder.Events.Length == 4, "the same states once more");
    }

    [Fact]
    public async Task A_trigger_event_not_newer_than_the_read_state_is_not_published_again()
    {
        await _server.StartAsync();
        SetStates(T0.AddMinutes(5), motion: "off");
        await StartGatewayAsync(url: _server.Url);
        await Eventually(() => _deskRecorder.Events.Length == 2, "the initial states");
        var connection = _server.Connections[0];

        await connection.SendTriggerEventAsync(DeskPower, "3.1", "83.4", T0.AddMinutes(5)); // the same change
        await connection.SendTriggerEventAsync(DeskPower, "3.1", "60", T0.AddMinutes(4)); // older
        await connection.SendTriggerEventAsync(DeskPower, "83.4", "4.2", T0.AddMinutes(6));
        await Eventually(() => _deskRecorder.Events.Length >= 3, "the newer change");
        await Quiet();

        Assert.Equal(3, _deskRecorder.Events.Length);
        Assert.Equal(4.2, Assert.IsType<DeskPowerChanged>(_deskRecorder.Events[^1]).Watts);
    }

    [Fact]
    public async Task An_unknown_entity_is_skipped_and_the_others_are_published()
    {
        await _server.StartAsync();
        SetStates(T0);
        _server.States.TryRemove(DeskSwitch, out _);

        await StartGatewayAsync(url: _server.Url);
        await Eventually(() => _deskRecorder.Events.Length == 1 && _recorder.Events.Length == 1, "the known states");
        await Quiet();

        Assert.IsType<DeskPowerChanged>(Assert.Single(_deskRecorder.Events));
        Assert.Equal(GatewayConnectionState.Connected, _gateway.Status.State);
        Assert.Single(_server.Connections);
    }

    [Fact]
    public async Task A_desk_switch_change_is_published_as_on_or_off_and_switching_off_also_as_its_own_event()
    {
        await StartGatewayAsync();
        await WaitConnected();
        var connection = _server.Connections[0];

        await connection.SendTriggerEventAsync(DeskSwitch, "off", "on", T0.AddMinutes(1));
        await connection.SendTriggerEventAsync(DeskSwitch, "on", "unavailable", T0.AddMinutes(2)); // not published
        await connection.SendTriggerEventAsync(DeskSwitch, "unavailable", "off", T0.AddMinutes(3));
        await Eventually(() => _deskRecorder.Events.Length >= 3, "two switch events and the switch-off");
        await Quiet();

        Assert.Collection(
            _deskRecorder.Events,
            e => { var on = Assert.IsType<DeskSwitched>(e); Assert.True(on.IsOn); Assert.Equal(T0.AddMinutes(1), on.ChangedAt); },
            e => { var off = Assert.IsType<DeskSwitched>(e); Assert.False(off.IsOn); Assert.Equal(T0.AddMinutes(3), off.ChangedAt); },
            e => Assert.Equal(T0.AddMinutes(3), Assert.IsType<DeskHasBeenSwitchedOffDomainEvent>(e).ChangedAt));
    }

    [Fact]
    public async Task A_motion_event_is_published_as_a_domain_event_with_the_time_Home_Assistant_gives()
    {
        await StartGatewayAsync();
        await WaitConnected();

        var changedAt = T0.AddMinutes(5);
        await _server.Connections[0].SendTriggerEventAsync(Motion, "off", "on", changedAt);
        await Eventually(() => _recorder.Events.Length == 1, "the domain event");

        var motion = Assert.Single(_recorder.Events);
        Assert.Equal(Motion, motion.EntityId);
        Assert.Equal(changedAt, motion.DetectedAt);
        Assert.NotEqual(Guid.Empty, motion.Id);
    }

    /// <summary>An event exactly as a real Home Assistant sent it for a state trigger (captured by the resident).</summary>
    private const string RealEvent = """
        {"id":1,"type":"event","event":{"variables":{"trigger":{"id":"0","idx":"0","alias":null,"platform":"state",
        "entity_id":"binary_sensor.living_room_eingang_occupancy",
        "from_state":{"entity_id":"binary_sensor.living_room_eingang_occupancy","state":"off","attributes":{"device_class":"occupancy","friendly_name":"Eingang Occupancy"},
          "last_changed":"2026-11-22T16:19:16.057680+00:00","last_reported":"2026-11-22T16:19:16.057680+00:00","last_updated":"2026-11-22T16:19:16.057680+00:00",
          "context":{"id":"01M820VZYST2CHH18ST8VJCF58","parent_id":null,"user_id":null}},
        "to_state":{"entity_id":"binary_sensor.living_room_eingang_occupancy","state":"on","attributes":{"device_class":"occupancy","friendly_name":"Eingang Occupancy"},
          "last_changed":"2026-11-22T18:13:46.304122+00:00","last_reported":"2026-11-22T18:13:46.304122+00:00","last_updated":"2026-11-22T18:13:46.304122+00:00",
          "context":{"id":"01M827DN605R57EGV75C7AM9BA","parent_id":null,"user_id":null}},
        "for":null,"attribute":null,"description":"state of binary_sensor.living_room_eingang_occupancy"}},
        "context":{"id":"01M827DN605R57EGV75C7AM9BA","parent_id":null,"user_id":null}}}
        """;

    [Fact]
    public async Task A_real_Home_Assistant_trigger_event_is_understood()
    {
        await StartGatewayAsync();
        await WaitConnected();

        await _server.Connections[0].SendAsync(RealEvent);
        await Eventually(() => _recorder.Events.Length == 1, "the real event");

        var motion = Assert.Single(_recorder.Events);
        Assert.Equal(Motion, motion.EntityId);
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 18, 13, 46, TimeSpan.Zero).AddTicks(3041220), motion.DetectedAt);
    }

    [Fact]
    public async Task Anything_but_motion_of_the_configured_sensor_is_not_published()
    {
        await StartGatewayAsync();
        await WaitConnected();
        var connection = _server.Connections[0];

        await connection.SendTriggerEventAsync("binary_sensor.other_room", "off", "on", T0.AddMinutes(1)); // other entity
        await connection.SendTriggerEventAsync(Motion, "on", "off", T0.AddMinutes(2)); // not "to on"
        await connection.SendTriggerEventAsync(Motion, "off", "on", T0.AddMinutes(3)); // marker
        await Eventually(() => _recorder.Events.Length >= 1, "the marker event");
        await Quiet();

        var motion = Assert.Single(_recorder.Events);
        Assert.Equal(T0.AddMinutes(3), motion.DetectedAt);
    }

    [Fact]
    public async Task A_failing_handler_does_not_take_the_connection_down()
    {
        _recorder.FailuresLeft = 1;
        await StartGatewayAsync();
        await WaitConnected();
        var connection = _server.Connections[0];

        await connection.SendTriggerEventAsync(Motion, "off", "on", T0.AddMinutes(1)); // handler throws
        await connection.SendTriggerEventAsync(Motion, "on", "on", T0.AddMinutes(2));
        await Eventually(() => _recorder.Events.Length == 1, "the second event");

        Assert.Equal(T0.AddMinutes(2), Assert.Single(_recorder.Events).DetectedAt);
        Assert.Equal(GatewayConnectionState.Connected, _gateway.Status.State);
        Assert.Single(_server.Connections);
    }

    [Fact]
    public async Task A_wrong_token_is_a_failure_and_is_not_retried()
    {
        await StartGatewayAsync(token: "wrong");
        await Eventually(() => _gateway.Status.State == GatewayConnectionState.AuthenticationFailed, "authentication failed");

        _time.Advance(TimeSpan.FromMinutes(10));
        await Quiet();

        Assert.Equal(1, _server.ConnectionAttempts);
        Assert.Equal(GatewayConnectionState.AuthenticationFailed, _gateway.Status.State);
    }

    [Fact]
    public async Task A_lost_connection_is_re_established_and_subscribed_again()
    {
        await StartGatewayAsync();
        await WaitConnected();

        _server.Connections[0].Drop();
        await Eventually(() => _gateway.Status.State == GatewayConnectionState.Disconnected, "disconnected");
        Assert.False(_gateway.Status.IsConnected);

        _time.Advance(TimeSpan.FromSeconds(1)); // the initial backoff
        await Eventually(() => _server.Connections.Count == 2, "second connection");
        await WaitConnected();

        Assert.Equal(["subscribe_trigger"], _server.Connections[1].Received);

        await _server.Connections[1].SendTriggerEventAsync(Motion, "off", "on", T0.AddMinutes(1));
        await Eventually(() => _recorder.Events.Length == 1, "motion after the reconnect");
    }

    [Fact]
    public async Task The_backoff_doubles_up_to_a_limit_while_Home_Assistant_is_down()
    {
        await _server.StartAsync();
        _server.RefuseConnections = true;
        await StartGatewayAsync(url: _server.Url, maxBackoff: "00:00:04");

        await Eventually(() => _server.ConnectionAttempts == 1, "first attempt");
        _time.Advance(TimeSpan.FromSeconds(1));
        await Eventually(() => _server.ConnectionAttempts == 2, "second attempt after 1 s");

        _time.Advance(TimeSpan.FromSeconds(1));
        await Quiet();
        Assert.Equal(2, _server.ConnectionAttempts); // the next wait is 2 s
        _time.Advance(TimeSpan.FromSeconds(1));
        await Eventually(() => _server.ConnectionAttempts == 3, "third attempt after 2 s");

        _time.Advance(TimeSpan.FromSeconds(3));
        await Quiet();
        Assert.Equal(3, _server.ConnectionAttempts); // the next wait is 4 s
        _time.Advance(TimeSpan.FromSeconds(1));
        await Eventually(() => _server.ConnectionAttempts == 4, "fourth attempt after 4 s");

        _time.Advance(TimeSpan.FromSeconds(4));
        await Eventually(() => _server.ConnectionAttempts == 5, "fifth attempt: the wait stays at the limit");
        Assert.Equal(GatewayConnectionState.Disconnected, _gateway.Status.State);
    }

    [Fact]
    public async Task The_backend_runs_without_Home_Assistant_and_connects_once_it_is_reachable()
    {
        await _server.StartAsync();
        var url = _server.Url;
        await _server.StopAsync();

        await StartGatewayAsync(url: url); // nothing listens: StartAsync must still return

        await Eventually(() => _gateway.Status.State == GatewayConnectionState.Disconnected, "disconnected while down");

        await _server.StartAsync();
        _time.Advance(TimeSpan.FromSeconds(1));

        await WaitConnected();
    }

    [Theory]
    [InlineData(null, Motion)]
    [InlineData(FakeHomeAssistantServer.ValidToken, null)]
    public async Task Without_token_or_motion_sensor_the_gateway_stays_off_and_says_so(string? token, string? motionSensor)
    {
        await StartGatewayAsync(token: token, motionSensor: motionSensor);

        Assert.Equal(GatewayConnectionState.NotConfigured, _gateway.Status.State);
        Assert.Equal(0, _server.ConnectionAttempts);
    }
}
