using Backend.Modules.Home.Infrastructure.HomeAssistant;
using GMB.Shared.Application.Messaging;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Backend.Modules.Home.Features.SwitchHomeOn;

public class SwitchHomeOnCommandHandler(
    IHomeAssistantGateway gateway,
    ILogger<SwitchHomeOnCommandHandler> logger)
    : IApplicationCommandHandler<SwitchHomeOnCommand>
{
    public async ValueTask<Unit> Handle(SwitchHomeOnCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Switching the home on.");

        await gateway.TurnAllSwitchesOnAsync(cancellationToken);
        return Unit.Value;
    }
}
