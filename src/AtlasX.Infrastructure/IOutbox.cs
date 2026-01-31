namespace AtlasX.Infrastructure;

/// <summary>
/// Defines a reliable outbox for integration events.
/// </summary>
public interface IOutbox
{
    /// <summary>
    /// Enqueues an integration event for later publishing.
    /// </summary>
    void Enqueue(IIntegrationEvent integrationEvent);

    /// <summary>
    /// Tries to lease up to the requested number of events for publishing.
    /// </summary>
    IReadOnlyList<OutboxRecord> TryLeaseBatch(DateTimeOffset now, int batchSize, TimeSpan leaseDuration);

    /// <summary>
    /// Marks the given events as published.
    /// </summary>
    void MarkPublished(IEnumerable<Guid> ids);

    /// <summary>
    /// Marks a single event as failed or rescheduled for retry.
    /// </summary>
    void MarkFailedOrReschedule(Guid id, string error, DateTimeOffset nextAttemptAt, OutboxStatus status);
}
