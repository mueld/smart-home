namespace Backend.Modules.Home.Infrastructure.HomeAssistant;

/// <summary>Configuration of the gateway (options pattern, AD-18). The entity ID and the token never live in code.</summary>
public sealed class HomeAssistantOptions
{
    public const string SectionName = "HomeAssistant";

    /// <summary>Base address, for example <c>http://host.docker.internal:8123</c>.</summary>
    public string Url { get; set; } = "";

    /// <summary>Long-lived access token. A secret from the environment (<c>HomeAssistant__Token</c>).</summary>
    public string Token { get; set; } = "";

    /// <summary>Entity ID of the motion sensor at the entrance, for example <c>binary_sensor.living_room_eingang_occupancy</c>.</summary>
    public string MotionSensorEntityId { get; set; } = "";

    /// <summary>Optional: the desk socket, for example <c>switch.living_room_schreibtisch</c>.</summary>
    public string DeskSwitchEntityId { get; set; } = "";

    /// <summary>Optional: the power sensor of the desk socket, for example <c>sensor.living_room_schreibtisch_power</c>.</summary>
    public string DeskPowerEntityId { get; set; } = "";

    /// <summary>The entities of the one state-trigger subscription: the motion sensor and the desk entities that are set.</summary>
    public IReadOnlyList<string> SubscribedEntityIds =>
        [.. new[] { MotionSensorEntityId, DeskSwitchEntityId, DeskPowerEntityId }.Where(id => !string.IsNullOrWhiteSpace(id))];

    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Url)
        && !string.IsNullOrWhiteSpace(Token)
        && !string.IsNullOrWhiteSpace(MotionSensorEntityId);
}
