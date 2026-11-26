namespace Backend.Modules.Home.Infrastructure.HomeAssistant;

public enum GatewayConnectionState
{
    /// <summary>No URL, token or motion sensor configured; the gateway does nothing.</summary>
    NotConfigured,

    Disconnected,

    Connecting,

    /// <summary>Authenticated and subscribed to the motion sensor and the desk.</summary>
    Connected,

    /// <summary>Home Assistant rejected the token. The gateway stays here until the backend restarts.</summary>
    AuthenticationFailed,
}

public sealed record GatewayStatus(GatewayConnectionState State, DateTimeOffset Since)
{
    public bool IsConnected => State == GatewayConnectionState.Connected;
}

/// <summary>
/// The Home Assistant gateway (AD-18). It publishes what it receives as domain events through the mediator
/// (<see cref="Domain.MotionDetectedDomainEvent"/>), reports here whether it is connected and switches the sockets.
/// </summary>
public interface IHomeAssistantGateway
{
    GatewayStatus Status { get; }

    /// <summary>
    /// Switches every configured socket on (<see cref="HomeAssistantOptions.AllSwitchEntityIds"/>), never other switches
    /// Home Assistant knows. Does nothing if the gateway is not configured or has no socket. A call Home Assistant refuses
    /// is only logged.
    /// </summary>
    /// <exception cref="HttpRequestException">Home Assistant is not reachable.</exception>
    Task TurnAllSwitchesOnAsync(CancellationToken cancellationToken);

    /// <summary>Like <see cref="TurnAllSwitchesOnAsync"/>, but switches off.</summary>
    Task TurnAllSwitchesOffAsync(CancellationToken cancellationToken);
}
