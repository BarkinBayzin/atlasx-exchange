using System.Collections.Concurrent;

namespace AtlasX.Infrastructure;

/// <summary>
/// Stores integration events in memory for later publishing.
/// </summary>
public sealed class InMemoryOutbox : IOutbox
{
    private readonly ConcurrentQueue<IIntegrationEvent> _queue = new();

    /// <inheritdoc />
    public void Enqueue(IIntegrationEvent integrationEvent)
    {
        if (integrationEvent is null)
        {
            throw new ArgumentNullException(nameof(integrationEvent));
        }

        _queue.Enqueue(integrationEvent);
    }

    /// <inheritdoc />
    public IReadOnlyList<IIntegrationEvent> TryDequeueBatch(int maxItems)
    {
        if (maxItems <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItems), "Max items must be greater than zero.");
        }

        var batch = new List<IIntegrationEvent>(maxItems);
        while (batch.Count < maxItems && _queue.TryDequeue(out var integrationEvent))
        {
            batch.Add(integrationEvent);
        }

        return batch;
    }
}
