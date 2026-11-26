using Mediator;

namespace GMB.Shared.Application.Messaging;

public abstract record IntegrationEvent() : INotification
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public DateTime OccuredAtUtc { get; init; } = DateTime.UtcNow;
}
