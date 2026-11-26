using Backend.Kernel.Abstractions;
using Backend.Modules.Home.Domain;
using Mediator;

namespace Backend.Modules.Home.Features.SwitchHomeOn;

public class HomecomingDetectedDomainEventHandler(ISender sender) : IDomainEventHandler<HomecomingDetectedDomainEvent>
{
    public async ValueTask Handle(HomecomingDetectedDomainEvent notification, CancellationToken cancellationToken)
    {
        await sender.Send(new SwitchHomeOnCommand(), cancellationToken);
    }
}
