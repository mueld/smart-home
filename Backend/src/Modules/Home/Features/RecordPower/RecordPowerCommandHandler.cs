using Backend.Modules.Home.Features.Shared;
using GMB.Shared.Application.Messaging;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Backend.Modules.Home.Features.RecordPower;

public class RecordPowerCommandHandler(
    IHomeDbContext dbContext,
    IOptions<PresenceOptions> options,
    ILogger<RecordPowerCommandHandler> logger)
    : IApplicationCommandHandler<RecordPowerCommand>
{
    public async ValueTask<Unit> Handle(RecordPowerCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Recording desk power of {Watts} W at {ReportedAt}.", command.watts, command.reportedAt);

        var presence = await dbContext.Presences.FirstAsync(cancellationToken);
        var previousStatus = presence.Status;
        presence.RecordPower(command.watts, options.Value.DeskBusyThreshold, command.reportedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogRecorded(presence, previousStatus);
        return Unit.Value;
    }
}
