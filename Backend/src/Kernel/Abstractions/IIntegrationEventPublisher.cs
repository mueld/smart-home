namespace GMB.Shared.Application.Messaging;

public interface IIntegrationEventPublisher
{
    /// <summary>
    /// Publishes the specified integration event to the message bus.
    /// </summary>
    /// <param name="integrationEvent">The integration event to publish.</param>
    /// <returns>A task that represents the asynchronous publish operation.</returns>
    Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken);
}
