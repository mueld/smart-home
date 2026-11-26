using Backend.Kernel.Abstractions;
using Backend.Modules.Home.Domain;
using Mediator;

namespace Backend.Modules.Home.Features.SwitchHomeOff;

public class BecameAwayDomainEventHandler(ISender sender) : IDomainEventHandler<BecameAwayDomainEvent>
{
    public async ValueTask Handle(BecameAwayDomainEvent notification, CancellationToken cancellationToken)
    {
        await sender.Send(new SwitchHomeOffCommand(), cancellationToken);
    }
}
