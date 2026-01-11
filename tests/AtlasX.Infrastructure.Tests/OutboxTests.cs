using AtlasX.Infrastructure;

namespace AtlasX.Infrastructure.Tests;

public class OutboxTests
{
    [Fact]
    public void Outbox_dequeues_in_fifo_batches()
    {
        var outbox = new InMemoryOutbox();
        var first = new TestEvent(Guid.NewGuid(), DateTime.UtcNow);
        var second = new TestEvent(Guid.NewGuid(), DateTime.UtcNow);
        var third = new TestEvent(Guid.NewGuid(), DateTime.UtcNow);

        outbox.Enqueue(first);
        outbox.Enqueue(second);
        outbox.Enqueue(third);

        var batch1 = outbox.TryDequeueBatch(2);
        var batch2 = outbox.TryDequeueBatch(2);
        var batch3 = outbox.TryDequeueBatch(2);

        Assert.Equal(2, batch1.Count);
        Assert.Equal(first.EventId, batch1[0].EventId);
        Assert.Equal(second.EventId, batch1[1].EventId);
        Assert.Single(batch2);
        Assert.Equal(third.EventId, batch2[0].EventId);
        Assert.Empty(batch3);
    }

    private sealed record TestEvent(Guid EventId, DateTime OccurredAtUtc) : IIntegrationEvent;
}
