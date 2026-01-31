using System.Linq;
using AtlasX.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AtlasX.Api;

internal sealed class OutboxPublisherService : BackgroundService
{
    private readonly IOutbox _outbox;
    private readonly IEventBus _eventBus;
    private readonly ILogger<OutboxPublisherService> _logger;
    private readonly OutboxOptions _options;

    public OutboxPublisherService(
        IOutbox outbox,
        IEventBus eventBus,
        IOptions<OutboxOptions> options,
        ILogger<OutboxPublisherService> logger)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessOnceAsync(DateTimeOffset.UtcNow, stoppingToken).ConfigureAwait(false);

            var pollDelay = TimeSpan.FromMilliseconds(Math.Max(1, _options.PollIntervalMs));
            await Task.Delay(pollDelay, stoppingToken).ConfigureAwait(false);
        }
    }

    internal async Task ProcessOnceAsync(DateTimeOffset now, CancellationToken stoppingToken)
    {
        var batchSize = Math.Max(1, _options.BatchSize);
        var leaseDuration = TimeSpan.FromSeconds(Math.Max(1, _options.LeaseSeconds));
        var leased = _outbox.TryLeaseBatch(now, batchSize, leaseDuration);
        if (leased.Count == 0)
        {
            return;
        }

        var maxParallelism = Math.Max(1, _options.MaxParallelism);
        using var semaphore = new SemaphoreSlim(maxParallelism, maxParallelism);
        var tasks = leased.Select(async record =>
        {
            await semaphore.WaitAsync(stoppingToken).ConfigureAwait(false);
            try
            {
                await PublishRecordAsync(record, now, stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task PublishRecordAsync(OutboxRecord record, DateTimeOffset now, CancellationToken stoppingToken)
    {
        try
        {
            var integrationEvent = IntegrationEventSerializer.Deserialize(record.TypeName, record.Payload);
            await _eventBus.PublishAsync(integrationEvent, stoppingToken).ConfigureAwait(false);
            _outbox.MarkPublished(new[] { record.Id });
        }
        catch (Exception ex)
        {
            var nextAttempt = record.Attempts + 1;
            var status = nextAttempt >= Math.Max(1, _options.MaxAttempts)
                ? OutboxStatus.Failed
                : OutboxStatus.Pending;
            var delay = ComputeBackoff(nextAttempt);
            var nextAttemptAt = now.Add(delay);

            _outbox.MarkFailedOrReschedule(record.Id, ex.Message, nextAttemptAt, status);
            _logger.LogWarning(ex, "Failed to publish outbox event {EventType} ({EventId}).", record.TypeName, record.Id);
        }
    }

    private TimeSpan ComputeBackoff(int attempt)
    {
        var baseDelayMs = Math.Max(1, _options.BaseRetryDelayMs);
        var maxDelayMs = Math.Max(baseDelayMs, _options.MaxRetryDelayMs);
        var exponent = Math.Clamp(attempt - 1, 0, 20);
        var delay = baseDelayMs * Math.Pow(2, exponent);
        var clamped = (int)Math.Min(delay, maxDelayMs);
        return TimeSpan.FromMilliseconds(clamped);
    }
}
