namespace AtlasX.Infrastructure;

/// <summary>
/// Defines an in-memory outbox for integration events.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Enqueues an integration event for later publishing.
    /// </summary>
    void Enqueue(IIntegrationEvent integrationEvent);

    /// <summary>
    /// Tries to dequeue up to the requested number of events.
    /// </summary>
    IReadOnlyList<IIntegrationEvent> TryDequeueBatch(int maxItems);
}
