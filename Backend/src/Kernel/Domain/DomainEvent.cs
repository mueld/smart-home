using Mediator;

namespace Backend.Kernel.Domain;

public interface IDomainEvent : INotification;
public record DomainEvent(): IDomainEvent
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
}
