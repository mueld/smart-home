using Backend.Kernel.Abstractions;
using Backend.Modules.Home.Domain;
using Mediator;

namespace Backend.Modules.Home.Features.RecordMotion;

public class MotionDetectedDomainEventHandler(ISender sender) : IDomainEventHandler<MotionDetectedDomainEvent>
{
    public async ValueTask Handle(MotionDetectedDomainEvent notification, CancellationToken cancellationToken)
    {
        await sender.Send(new RecordMotionCommand(notification.DetectedAt), cancellationToken);
    }
}
