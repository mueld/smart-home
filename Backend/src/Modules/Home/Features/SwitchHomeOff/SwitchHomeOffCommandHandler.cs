using Backend.Modules.Home.Infrastructure.HomeAssistant;
using GMB.Shared.Application.Messaging;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Backend.Modules.Home.Features.SwitchHomeOff;

public class SwitchHomeOffCommandHandler(
    IHomeAssistantGateway gateway,
    ILogger<SwitchHomeOffCommandHandler> logger)
    : IApplicationCommandHandler<SwitchHomeOffCommand>
{
    public async ValueTask<Unit> Handle(SwitchHomeOffCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Switching the home off.");

        await gateway.TurnAllSwitchesOffAsync(cancellationToken);
        return Unit.Value;
    }
}
