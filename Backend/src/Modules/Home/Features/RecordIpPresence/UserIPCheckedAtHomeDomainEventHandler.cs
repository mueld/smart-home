using Backend.Kernel.Abstractions;
using Backend.Modules.Home.Domain;
using Mediator;

namespace Backend.Modules.Home.Features.RecordIpPresence;

internal class UserIPCheckedAtHomeDomainEventHandler(ISender sender) : IDomainEventHandler<UserIPCheckedAtHomeDomainEvent>
{
    public async ValueTask Handle(UserIPCheckedAtHomeDomainEvent notification, CancellationToken cancellationToken)
    {
        await sender.Send(new RecordIpPresenceCommand(notification.reachable, notification.checkedAt));
    }
}
