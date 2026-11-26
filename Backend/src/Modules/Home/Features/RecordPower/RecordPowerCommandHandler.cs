using GMB.Shared.Application.Messaging;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Backend.Modules.Home.Features.RecordPower;

public class RecordPowerCommandHandler(IHomeDbContext dbContext, IOptions<PresenceOptions> options)
    : IApplicationCommandHandler<RecordPowerCommand>
{
    public async ValueTask<Unit> Handle(RecordPowerCommand command, CancellationToken cancellationToken)
    {
        var presence = await dbContext.Presences.FirstAsync(cancellationToken);
        presence.RecordPower(command.watts, options.Value.DeskBusyThreshold, command.reportedAt);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
