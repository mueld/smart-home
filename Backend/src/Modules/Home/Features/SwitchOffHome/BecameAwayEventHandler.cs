using Backend.Kernel.Abstractions;
using Backend.Modules.Home.Domain;
using Microsoft.Extensions.Logging;

namespace Backend.Modules.Home.Features.SwitchOffHome;

public class BecameAwayEventHandler(ILogger<BecameAwayEventHandler> logger) : IDomainEventHandler<BecameAwayDomainEvent>
{
    public ValueTask Handle(BecameAwayDomainEvent notification, CancellationToken cancellationToken)
    {
        logger.LogWarning("Switch off home");
        return ValueTask.CompletedTask;
    }
}
