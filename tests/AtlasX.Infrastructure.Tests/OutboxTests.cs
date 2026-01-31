using System.Linq;
using AtlasX.Infrastructure;

namespace AtlasX.Infrastructure.Tests;

public class OutboxTests
{
    [Fact]
    public void Outbox_leases_in_fifo_batches()
    {
        var outbox = new InMemoryOutbox();
        var first = new OrderAccepted(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "BTC-USD", "BUY", "LIMIT", 1m, 100m);
        var second = new OrderMatched(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "BTC-USD", 1);
        var third = new TradeSettled(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "BTC-USD", 100m, 1m, Guid.NewGuid(), Guid.NewGuid());

        outbox.Enqueue(first);
        outbox.Enqueue(second);
        outbox.Enqueue(third);

        var now = DateTimeOffset.UtcNow;
        var batch1 = outbox.TryLeaseBatch(now, 2, TimeSpan.FromSeconds(10));
        outbox.MarkPublished(batch1.Select(record => record.Id));
        var batch2 = outbox.TryLeaseBatch(now.AddSeconds(1), 2, TimeSpan.FromSeconds(10));
        outbox.MarkPublished(batch2.Select(record => record.Id));
        var batch3 = outbox.TryLeaseBatch(now.AddSeconds(2), 2, TimeSpan.FromSeconds(10));

        Assert.Equal(2, batch1.Count);
        var batch1First = IntegrationEventSerializer.Deserialize(batch1[0].TypeName, batch1[0].Payload);
        var batch1Second = IntegrationEventSerializer.Deserialize(batch1[1].TypeName, batch1[1].Payload);
        Assert.Equal(first.EventId, ((OrderAccepted)batch1First).EventId);
        Assert.Equal(second.EventId, ((OrderMatched)batch1Second).EventId);
        Assert.Single(batch2);
        var batch2Event = IntegrationEventSerializer.Deserialize(batch2[0].TypeName, batch2[0].Payload);
        Assert.Equal(third.EventId, ((TradeSettled)batch2Event).EventId);
        Assert.Empty(batch3);
    }
}
