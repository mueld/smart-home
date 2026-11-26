using Backend.Kernel.Abstractions;
using Backend.Modules.Home.Domain;
using Mediator;

namespace Backend.Modules.Home.Features.RecordPower;

public class DeskPowerChangedDomainEventHandler(ISender sender) : IDomainEventHandler<DeskPowerChanged>
{
    public async ValueTask Handle(DeskPowerChanged notification, CancellationToken cancellationToken)
    {
        await sender.Send(new RecordPowerCommand(notification.Watts, notification.ChangedAt), cancellationToken);
    }
}
