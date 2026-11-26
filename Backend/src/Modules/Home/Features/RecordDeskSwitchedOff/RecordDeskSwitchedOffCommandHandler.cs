using Backend.Modules.Home.Features.Shared;
using GMB.Shared.Application.Messaging;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Modules.Home.Features.RecordDeskSwitchedOff;

public class RecordDeskSwitchedOffCommandHandler(
    IHomeDbContext dbContext,
    ILogger<RecordDeskSwitchedOffCommandHandler> logger)
    : IApplicationCommandHandler<RecordDeskSwitchedOffCommand>
{
    public async ValueTask<Unit> Handle(RecordDeskSwitchedOffCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Recording that the desk was switched off at {ReportedAt}.", command.reportedAt);

        var presence = await dbContext.Presences.FirstAsync(cancellationToken);
        var previousStatus = presence.Status;
        presence.DeskHasBeenSwitchedOff(command.reportedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogRecorded(presence, previousStatus);
        return Unit.Value;
    }
}
