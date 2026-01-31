namespace AtlasX.Api;

internal sealed class IdempotencyStore
{
    private readonly Dictionary<IdempotencyKey, IdempotencyEntry> _entries = new();
    private readonly object _gate = new();
    private readonly TimeSpan _ttl;
    private readonly int _maxKeysTotal;
    private readonly int _maxKeysPerClient;

    internal IdempotencyStore(IdempotencyOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _ttl = options.DefaultTtlSeconds > 0
            ? TimeSpan.FromSeconds(options.DefaultTtlSeconds)
            : TimeSpan.FromHours(24);
        _maxKeysTotal = options.MaxKeysTotal > 0 ? options.MaxKeysTotal : 10_000;
        _maxKeysPerClient = options.MaxKeysPerClient > 0 ? options.MaxKeysPerClient : 1_000;
    }

    internal bool TryGet(string clientId, string key, out IdempotencyEntry entry)
    {
        var idempotencyKey = new IdempotencyKey(clientId, key);
        lock (_gate)
        {
            if (_entries.TryGetValue(idempotencyKey, out entry))
            {
                if (entry.ExpiresAtUtc <= DateTime.UtcNow)
                {
                    _entries.Remove(idempotencyKey);
                    entry = default;
                    return false;
                }

                return true;
            }
        }

        entry = default;
        return false;
    }

    internal void Store(string clientId, string key, int statusCode, object payload)
    {
        var now = DateTime.UtcNow;
        var idempotencyKey = new IdempotencyKey(clientId, key);
        var entry = new IdempotencyEntry(statusCode, payload, now, now.Add(_ttl));

        lock (_gate)
        {
            RemoveExpired(now);
            _entries[idempotencyKey] = entry;
            EnforceCapacity(clientId);
        }
    }

    internal int CountEntries()
    {
        lock (_gate)
        {
            return _entries.Count;
        }
    }

    internal int CountEntriesForClient(string clientId)
    {
        lock (_gate)
        {
            return _entries.Count(entry => entry.Key.ClientId == clientId);
        }
    }

    private void EnforceCapacity(string clientId)
    {
        while (_entries.Count > _maxKeysTotal)
        {
            if (!TryRemoveOldest(null))
            {
                break;
            }
        }

        while (CountEntriesForClientNoLock(clientId) > _maxKeysPerClient)
        {
            if (!TryRemoveOldest(clientId))
            {
                break;
            }
        }
    }

    private int CountEntriesForClientNoLock(string clientId)
    {
        return _entries.Count(entry => entry.Key.ClientId == clientId);
    }

    private bool TryRemoveOldest(string? clientId)
    {
        KeyValuePair<IdempotencyKey, IdempotencyEntry>? oldest = null;

        foreach (var entry in _entries)
        {
            if (clientId is not null && !string.Equals(entry.Key.ClientId, clientId, StringComparison.Ordinal))
            {
                continue;
            }

            if (oldest is null || entry.Value.CreatedAtUtc < oldest.Value.Value.CreatedAtUtc)
            {
                oldest = entry;
            }
        }

        if (oldest is null)
        {
            return false;
        }

        return _entries.Remove(oldest.Value.Key);
    }

    private void RemoveExpired(DateTime now)
    {
        var expiredKeys = new List<IdempotencyKey>();
        foreach (var entry in _entries)
        {
            if (entry.Value.ExpiresAtUtc <= now)
            {
                expiredKeys.Add(entry.Key);
            }
        }

        foreach (var key in expiredKeys)
        {
            _entries.Remove(key);
        }
    }

    internal readonly record struct IdempotencyEntry(
        int StatusCode,
        object Payload,
        DateTime CreatedAtUtc,
        DateTime ExpiresAtUtc);

    private readonly record struct IdempotencyKey(string ClientId, string Key);
}
