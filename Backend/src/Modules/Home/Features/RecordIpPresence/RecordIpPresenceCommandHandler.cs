using GMB.Shared.Application.Messaging;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Backend.Modules.Home.Features.RecordIpPresence;

public class RecordIpPresenceCommandHandler(IHomeDbContext dbContext) : IApplicationCommandHandler<RecordIpPresenceCommand>
{
    public async ValueTask<Unit> Handle(RecordIpPresenceCommand command, CancellationToken cancellationToken)
    {
        var precense = await dbContext.Presences.FirstAsync(cancellationToken);
        precense.RecordIpPresence(command.reachable, command.checkedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
