using Backend.Modules.Home.Infrastructure.HomeAssistant;

namespace Backend.Tests.Home;

/// <summary>
/// The fake of <see cref="IHomeAssistantGateway"/> for the tests of other Home slices: tests set the connection
/// status; no Home Assistant, no network. Motion needs no fake: a test publishes
/// <see cref="Backend.Modules.Home.Domain.MotionDetected"/> through the mediator.
/// </summary>
public sealed class FakeHomeAssistantGateway : IHomeAssistantGateway
{
    public GatewayStatus Status { get; private set; } = new(GatewayConnectionState.Connected, DateTimeOffset.UnixEpoch);

    public void SetStatus(GatewayConnectionState state, DateTimeOffset since) => Status = new GatewayStatus(state, since);
}
