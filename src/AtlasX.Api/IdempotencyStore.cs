using System.Collections.Concurrent;

namespace AtlasX.Api;

internal sealed class IdempotencyStore
{
    private readonly ConcurrentDictionary<IdempotencyKey, IdempotencyEntry> _entries = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    internal bool TryGet(string clientId, string key, out IdempotencyEntry entry)
    {
        var idempotencyKey = new IdempotencyKey(clientId, key);
        if (_entries.TryGetValue(idempotencyKey, out entry))
        {
            if (entry.ExpiresAtUtc <= DateTime.UtcNow)
            {
                _entries.TryRemove(idempotencyKey, out _);
                entry = default;
                return false;
            }

            return true;
        }

        entry = default;
        return false;
    }

    internal void Store(string clientId, string key, int statusCode, object payload)
    {
        var entry = new IdempotencyEntry(statusCode, payload, DateTime.UtcNow.Add(Ttl));
        var idempotencyKey = new IdempotencyKey(clientId, key);
        _entries[idempotencyKey] = entry;
        RemoveExpired();
    }

    private void RemoveExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var item in _entries)
        {
            if (item.Value.ExpiresAtUtc <= now)
            {
                _entries.TryRemove(item.Key, out _);
            }
        }
    }

    internal readonly record struct IdempotencyEntry(int StatusCode, object Payload, DateTime ExpiresAtUtc);

    private readonly record struct IdempotencyKey(string ClientId, string Key);
}
