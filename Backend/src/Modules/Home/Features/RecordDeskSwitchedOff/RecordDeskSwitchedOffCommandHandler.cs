using GMB.Shared.Application.Messaging;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Home.Features.RecordDeskSwitchedOff;

public class RecordDeskSwitchedOffCommandHandler(IHomeDbContext dbContext)
    : IApplicationCommandHandler<RecordDeskSwitchedOffCommand>
{
    public async ValueTask<Unit> Handle(RecordDeskSwitchedOffCommand command, CancellationToken cancellationToken)
    {
        var presence = await dbContext.Presences.FirstAsync(cancellationToken);
        presence.DeskHasBeenSwitchedOff(command.reportedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
