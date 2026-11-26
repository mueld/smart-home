using Backend.Kernel.Abstractions;
using Backend.Modules.Home.Domain;
using Microsoft.Extensions.Logging;

namespace Backend.Modules.Home.Features.SwitchHomeOn;

public class HomecomingDetectedDomainEventHandler(ILogger<HomecomingDetectedDomainEventHandler> logger) : IDomainEventHandler<HomecomingDetectedDomainEvent>
{
    public ValueTask Handle(HomecomingDetectedDomainEvent notification, CancellationToken cancellationToken)
    {
        logger.LogWarning("User is coming home");
        return ValueTask.CompletedTask;
    }
}
