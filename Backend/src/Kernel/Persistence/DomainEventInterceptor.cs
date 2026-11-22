using Backend.Kernel.Domain;
using Mediator;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Backend.Kernel.Persistence;

/// <summary>
/// Publishes the domain events of the saved aggregates in the SavedChanges hook (AD-6), still inside the
/// transaction of the running command, so everything an event handler does is committed or rolled back with it.
/// An event handler may send commands (they join the transaction, AD-14) or call SaveChangesAsync itself; the
/// events raised by that save are published recursively before the outer SaveChanges returns.
/// Only the async hook is supported; saving is async only.
/// </summary>
internal sealed class DomainEventInterceptor(IPublisher publisher, TransactionCoordinator coordinator) : SaveChangesInterceptor
{
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            var events = context.ChangeTracker.Entries<AggregateRoot>()
                .ToArray()
                .SelectMany(entry => entry.Entity.TakeDomainEvents())
                .ToArray();

            if (events.Length > 0)
            {
                using var frame = coordinator.BeginEventDispatch();

                foreach (var domainEvent in events)
                {
                    await publisher.Publish((object)domainEvent, cancellationToken);
                }
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        => throw new InvalidOperationException("Use SaveChangesAsync. Synchronous saving is not allowed (AD-6).");
}
