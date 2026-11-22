using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Kernel.Domain;

public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; init; } = Guid.CreateVersion7();

    [NotMapped]
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents;

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    internal IDomainEvent[] TakeDomainEvents()
    {
        var events = _domainEvents.ToArray();
        _domainEvents.Clear();
        return events;
    }
}
