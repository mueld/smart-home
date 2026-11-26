using Backend.Kernel.Domain;
using Mediator;

namespace Backend.Kernel.Abstractions;

public interface IDomainEventHandler<TDomainEvent> : INotificationHandler<TDomainEvent>
    where TDomainEvent : IDomainEvent;