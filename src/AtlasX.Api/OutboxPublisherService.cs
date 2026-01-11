using AtlasX.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AtlasX.Api;

internal sealed class OutboxPublisherService : BackgroundService
{
    private readonly IOutbox _outbox;
    private readonly IEventBus _eventBus;
    private readonly ILogger<OutboxPublisherService> _logger;

    public OutboxPublisherService(IOutbox outbox, IEventBus eventBus, ILogger<OutboxPublisherService> logger)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var batch = _outbox.TryDequeueBatch(100);
            foreach (var integrationEvent in batch)
            {
                try
                {
                    await _eventBus.PublishAsync(integrationEvent, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to publish outbox event {EventType}.", integrationEvent.GetType().Name);
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
        }
    }
}
