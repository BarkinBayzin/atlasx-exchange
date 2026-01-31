using System.Threading;
using System.Threading.Tasks;
using AtlasX.Api;

namespace AtlasX.Api.Tests;

public class IdempotencyStoreTests
{
    [Fact]
    public void Expired_entry_is_not_returned()
    {
        var options = new IdempotencyOptions
        {
            DefaultTtlSeconds = 1,
            MaxKeysTotal = 10,
            MaxKeysPerClient = 10
        };
        var store = new IdempotencyStore(options);

        store.Store("client-1", "key-1", 200, new { ok = true });

        Thread.Sleep(1100);

        Assert.False(store.TryGet("client-1", "key-1", out _));
        Assert.Equal(0, store.CountEntries());
    }

    [Fact]
    public void Capacity_eviction_removes_oldest_entries()
    {
        var options = new IdempotencyOptions
        {
            DefaultTtlSeconds = 3600,
            MaxKeysTotal = 2,
            MaxKeysPerClient = 2
        };
        var store = new IdempotencyStore(options);

        store.Store("client-1", "key-1", 200, new { ok = true });
        Thread.Sleep(5);
        store.Store("client-1", "key-2", 200, new { ok = true });
        Thread.Sleep(5);
        store.Store("client-1", "key-3", 200, new { ok = true });

        Assert.False(store.TryGet("client-1", "key-1", out _));
        Assert.True(store.TryGet("client-1", "key-2", out _));
        Assert.True(store.TryGet("client-1", "key-3", out _));
        Assert.Equal(2, store.CountEntries());
    }

    [Fact]
    public void Concurrent_inserts_respect_capacity()
    {
        var options = new IdempotencyOptions
        {
            DefaultTtlSeconds = 3600,
            MaxKeysTotal = 50,
            MaxKeysPerClient = 50
        };
        var store = new IdempotencyStore(options);

        Parallel.For(0, 100, i =>
        {
            store.Store("client-1", $"key-{i}", 200, new { ok = true });
        });

        Assert.True(store.CountEntries() <= 50);
        Assert.True(store.CountEntriesForClient("client-1") <= 50);
    }
}
