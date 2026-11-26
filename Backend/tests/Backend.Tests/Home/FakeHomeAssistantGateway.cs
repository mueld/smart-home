using Backend.Modules.Home.Infrastructure.HomeAssistant;

namespace Backend.Tests.Home;

/// <summary>
/// The fake of <see cref="IHomeAssistantGateway"/> for the tests of other Home slices: tests set the connection
/// status; no Home Assistant, no network. Motion needs no fake: a test publishes
/// <see cref="Backend.Modules.Home.Domain.MotionDetectedDomainEvent"/> through the mediator.
/// </summary>
public sealed class FakeHomeAssistantGateway : IHomeAssistantGateway
{
    public GatewayStatus Status { get; private set; } = new(GatewayConnectionState.Connected, DateTimeOffset.UnixEpoch);

    public void SetStatus(GatewayConnectionState state, DateTimeOffset since) => Status = new GatewayStatus(state, since);

    /// <summary>The switch calls in order: "on" or "off".</summary>
    public List<string> SwitchCalls { get; } = [];

    public Task TurnAllSwitchesOnAsync(CancellationToken cancellationToken)
    {
        SwitchCalls.Add("on");
        return Task.CompletedTask;
    }

    public Task TurnAllSwitchesOffAsync(CancellationToken cancellationToken)
    {
        SwitchCalls.Add("off");
        return Task.CompletedTask;
    }
}
