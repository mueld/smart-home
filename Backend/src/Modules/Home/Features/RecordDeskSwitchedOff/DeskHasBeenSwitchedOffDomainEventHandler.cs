using Backend.Kernel.Abstractions;
using Backend.Modules.Home.Domain;
using Mediator;

namespace Backend.Modules.Home.Features.RecordDeskSwitchedOff;

public class DeskHasBeenSwitchedOffDomainEventHandler(ISender sender)
    : IDomainEventHandler<DeskHasBeenSwitchedOffDomainEvent>
{
    public async ValueTask Handle(DeskHasBeenSwitchedOffDomainEvent notification, CancellationToken cancellationToken)
    {
        await sender.Send(new RecordDeskSwitchedOffCommand(notification.ChangedAt), cancellationToken);
    }
}
