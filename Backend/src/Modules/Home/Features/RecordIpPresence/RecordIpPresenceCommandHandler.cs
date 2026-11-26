using Backend.Modules.Home.Features.Shared;
using GMB.Shared.Application.Messaging;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Modules.Home.Features.RecordIpPresence;

public class RecordIpPresenceCommandHandler(
    IHomeDbContext dbContext,
    ILogger<RecordIpPresenceCommandHandler> logger)
    : IApplicationCommandHandler<RecordIpPresenceCommand>
{
    public async ValueTask<Unit> Handle(RecordIpPresenceCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Recording IP check at {CheckedAt}, reachable: {Reachable}.", command.checkedAt, command.reachable);

        var precense = await dbContext.Presences.FirstAsync(cancellationToken);
        var previousStatus = precense.Status;
        precense.RecordIpPresence(command.reachable, command.checkedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogRecorded(precense, previousStatus);
        return Unit.Value;
    }
}
