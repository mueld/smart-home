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
/// (<see cref="Domain.MotionDetected"/>) and only reports here whether it is connected. Switching sockets
/// (service calls) is added by a later story.
/// </summary>
public interface IHomeAssistantGateway
{
    GatewayStatus Status { get; }
}
