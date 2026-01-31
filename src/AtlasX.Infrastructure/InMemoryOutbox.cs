using System.Linq;

namespace AtlasX.Infrastructure;

/// <summary>
/// Stores integration events in memory for later publishing.
/// </summary>
public sealed class InMemoryOutbox : IOutbox
{
    private readonly Dictionary<Guid, OutboxRecord> _records = new();
    private readonly object _gate = new();

    /// <inheritdoc />
    public void Enqueue(IIntegrationEvent integrationEvent)
    {
        if (integrationEvent is null)
        {
            throw new ArgumentNullException(nameof(integrationEvent));
        }

        var now = DateTimeOffset.UtcNow;
        var payload = IntegrationEventSerializer.Serialize(integrationEvent);
        var record = new OutboxRecord(
            Guid.NewGuid(),
            integrationEvent.GetType().Name,
            payload,
            now,
            OutboxStatus.Pending,
            Attempts: 0,
            NextAttemptAt: now,
            LockedUntil: DateTimeOffset.MinValue,
            LastError: null);

        lock (_gate)
        {
            _records[record.Id] = record;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<OutboxRecord> TryLeaseBatch(DateTimeOffset now, int batchSize, TimeSpan leaseDuration)
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "Batch size must be greater than zero.");
        }

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be greater than zero.");
        }

        lock (_gate)
        {
            var leased = _records.Values
                .Where(record =>
                    record.NextAttemptAt <= now &&
                    record.Status != OutboxStatus.Published &&
                    record.Status != OutboxStatus.Failed &&
                    record.LockedUntil <= now)
                .OrderBy(record => record.CreatedAt)
                .ThenBy(record => record.Id)
                .Take(batchSize)
                .ToList();

            if (leased.Count == 0)
            {
                return Array.Empty<OutboxRecord>();
            }

            var lockedUntil = now.Add(leaseDuration);
            foreach (var record in leased)
            {
                _records[record.Id] = record with
                {
                    Status = OutboxStatus.InFlight,
                    LockedUntil = lockedUntil
                };
            }

            return leased;
        }
    }

    /// <inheritdoc />
    public void MarkPublished(IEnumerable<Guid> ids)
    {
        if (ids is null)
        {
            throw new ArgumentNullException(nameof(ids));
        }

        lock (_gate)
        {
            foreach (var id in ids)
            {
                if (_records.TryGetValue(id, out var record))
                {
                    _records[id] = record with
                    {
                        Status = OutboxStatus.Published,
                        LockedUntil = DateTimeOffset.MinValue,
                        LastError = null
                    };
                }
            }
        }
    }

    /// <inheritdoc />
    public void MarkFailedOrReschedule(Guid id, string error, DateTimeOffset nextAttemptAt, OutboxStatus status)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            throw new ArgumentException("Error must be provided.", nameof(error));
        }

        if (status != OutboxStatus.Pending && status != OutboxStatus.Failed)
        {
            throw new ArgumentOutOfRangeException(nameof(status), "Status must be Pending or Failed.");
        }

        lock (_gate)
        {
            if (_records.TryGetValue(id, out var record))
            {
                _records[id] = record with
                {
                    Attempts = record.Attempts + 1,
                    Status = status,
                    NextAttemptAt = nextAttemptAt,
                    LockedUntil = DateTimeOffset.MinValue,
                    LastError = error
                };
            }
        }
    }
}
