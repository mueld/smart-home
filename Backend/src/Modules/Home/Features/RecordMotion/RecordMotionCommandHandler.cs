using Backend.Modules.Home.Features.Shared;
using GMB.Shared.Application.Messaging;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Backend.Modules.Home.Features.RecordMotion;

public class RecordMotionCommandHandler(
    IHomeDbContext dbContext,
    ILogger<RecordMotionCommandHandler> logger)
    : IApplicationCommandHandler<RecordMotionCommand>
{
    public async ValueTask<Unit> Handle(RecordMotionCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Recording motion at {DetectedAt}.", command.detectedAt);

        var presence = await dbContext.Presences.FirstAsync(cancellationToken);
        var previousStatus = presence.Status;
        presence.RecordMotion(command.detectedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogRecorded(presence, previousStatus);
        return Unit.Value;
    }
}
