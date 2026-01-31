using AtlasX.Api;
using AtlasX.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtlasX.Api.Tests;

public class OutboxPublisherServiceTests
{
    [Fact]
    public async Task Successful_publish_marks_record_published()
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

        var options = Options.Create(new OutboxOptions
        {
            BatchSize = 10,
            LeaseSeconds = 5,
            MaxAttempts = 3,
            BaseRetryDelayMs = 10,
            MaxRetryDelayMs = 10,
            MaxParallelism = 2
        });

        var eventBus = new TestEventBus();
        var service = new OutboxPublisherService(outbox, eventBus, options, NullLogger<OutboxPublisherService>.Instance);

        var now = DateTimeOffset.UtcNow;
        await service.ProcessOnceAsync(now, CancellationToken.None);

        var leasedAgain = outbox.TryLeaseBatch(now.AddSeconds(10), 1, TimeSpan.FromSeconds(5));
        Assert.Empty(leasedAgain);
        Assert.Equal(1, eventBus.PublishCount);
    }

    [Fact]
    public async Task Failed_publish_reschedules_with_backoff()
    {
        var outbox = new InMemoryOutbox();
        outbox.Enqueue(new OrderMatched(
            Guid.NewGuid(),
            DateTime.UtcNow,
            Guid.NewGuid(),
            "BTC-USD",
            1));

        var options = Options.Create(new OutboxOptions
        {
            BatchSize = 10,
            LeaseSeconds = 5,
            MaxAttempts = 3,
            BaseRetryDelayMs = 1000,
            MaxRetryDelayMs = 1000,
            MaxParallelism = 1
        });

        var eventBus = new TestEventBus { ShouldFail = true };
        var service = new OutboxPublisherService(outbox, eventBus, options, NullLogger<OutboxPublisherService>.Instance);

        var now = DateTimeOffset.UtcNow;
        await service.ProcessOnceAsync(now, CancellationToken.None);

        var immediate = outbox.TryLeaseBatch(now.AddMilliseconds(500), 1, TimeSpan.FromSeconds(5));
        Assert.Empty(immediate);

        var later = outbox.TryLeaseBatch(now.AddMilliseconds(1200), 1, TimeSpan.FromSeconds(5));
        var retried = Assert.Single(later);
        Assert.Equal(1, retried.Attempts);
    }

    [Fact]
    public async Task After_max_attempts_record_is_failed()
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

        var options = Options.Create(new OutboxOptions
        {
            BatchSize = 10,
            LeaseSeconds = 5,
            MaxAttempts = 1,
            BaseRetryDelayMs = 1,
            MaxRetryDelayMs = 1,
            MaxParallelism = 1
        });

        var eventBus = new TestEventBus { ShouldFail = true };
        var service = new OutboxPublisherService(outbox, eventBus, options, NullLogger<OutboxPublisherService>.Instance);

        var now = DateTimeOffset.UtcNow;
        await service.ProcessOnceAsync(now, CancellationToken.None);

        var later = outbox.TryLeaseBatch(now.AddSeconds(5), 1, TimeSpan.FromSeconds(5));
        Assert.Empty(later);
        Assert.Equal(1, eventBus.PublishCount);
    }

    private sealed class TestEventBus : IEventBus
    {
        public bool ShouldFail { get; set; }
        public int PublishCount { get; private set; }

        public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
        {
            PublishCount++;
            if (ShouldFail)
            {
                throw new InvalidOperationException("Simulated publish failure.");
            }

            return Task.CompletedTask;
        }
    }
}
