using AtlasX.Infrastructure;

namespace AtlasX.Infrastructure.Tests;

public class InMemoryOutboxTests
{
    [Fact]
    public void Leasing_prevents_double_processing_until_lock_expires()
    {
        var outbox = new InMemoryOutbox();
        outbox.Enqueue(new OrderAccepted(
            Guid.NewGuid(),
            DateTime.UtcNow,
            Guid.NewGuid(),
            "BTC-USD",
            "BUY",
            "LIMIT",
            1m,
            100m));

        var now = DateTimeOffset.UtcNow;
        var firstLease = outbox.TryLeaseBatch(now, 1, TimeSpan.FromSeconds(30));
        var secondLease = outbox.TryLeaseBatch(now, 1, TimeSpan.FromSeconds(30));

        Assert.Single(firstLease);
        Assert.Empty(secondLease);

        var afterLease = outbox.TryLeaseBatch(now.AddSeconds(31), 1, TimeSpan.FromSeconds(30));
        Assert.Single(afterLease);
    }

    [Fact]
    public void Reschedule_sets_next_attempt_and_increments_attempts()
    {
        var outbox = new InMemoryOutbox();
        outbox.Enqueue(new OrderMatched(
            Guid.NewGuid(),
            DateTime.UtcNow,
            Guid.NewGuid(),
            "BTC-USD",
            1));

        var now = DateTimeOffset.UtcNow;
        var leased = outbox.TryLeaseBatch(now, 1, TimeSpan.FromSeconds(5));
        var record = Assert.Single(leased);

        var nextAttemptAt = now.AddSeconds(10);
        outbox.MarkFailedOrReschedule(record.Id, "publish failed", nextAttemptAt, OutboxStatus.Pending);

        var immediate = outbox.TryLeaseBatch(now.AddSeconds(1), 1, TimeSpan.FromSeconds(5));
        Assert.Empty(immediate);

        var retried = outbox.TryLeaseBatch(nextAttemptAt.AddSeconds(1), 1, TimeSpan.FromSeconds(5));
        var retriedRecord = Assert.Single(retried);
        Assert.Equal(1, retriedRecord.Attempts);
    }

    [Fact]
    public void Published_records_are_not_leased_again()
    {
        var outbox = new InMemoryOutbox();
        outbox.Enqueue(new TradeSettled(
            Guid.NewGuid(),
            DateTime.UtcNow,
            Guid.NewGuid(),
            "BTC-USD",
            100m,
            1m,
            Guid.NewGuid(),
            Guid.NewGuid()));

        var now = DateTimeOffset.UtcNow;
        var leased = outbox.TryLeaseBatch(now, 1, TimeSpan.FromSeconds(5));
        var record = Assert.Single(leased);

        outbox.MarkPublished(new[] { record.Id });

        var later = outbox.TryLeaseBatch(now.AddSeconds(10), 1, TimeSpan.FromSeconds(5));
        Assert.Empty(later);
    }
}
