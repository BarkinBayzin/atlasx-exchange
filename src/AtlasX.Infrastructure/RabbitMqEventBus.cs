using System.Text;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using Microsoft.Extensions.Options;

namespace AtlasX.Infrastructure;

/// <summary>
/// Publishes integration events to RabbitMQ.
/// </summary>
public sealed class RabbitMqEventBus : IEventBus
{
    private const string ExchangeName = "atlasx.events";
    private readonly ILogger<RabbitMqEventBus> _logger;
    private readonly IRabbitMqConnectionManager _connectionManager;
    private readonly RabbitMqOptions _options;

    /// <summary>
    /// Initializes a new instance of the event bus.
    /// </summary>
    public RabbitMqEventBus(
        IRabbitMqConnectionManager connectionManager,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqEventBus> logger)
    {
        _connectionManager = connectionManager ?? throw new ArgumentNullException(nameof(connectionManager));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent is null)
        {
            throw new ArgumentNullException(nameof(integrationEvent));
        }

        try
        {
            var payload = IntegrationEventSerializer.Serialize(integrationEvent);
            var body = Encoding.UTF8.GetBytes(payload);
            using var channel = await _connectionManager.RentChannelAsync(cancellationToken).ConfigureAwait(false);
            channel.ExchangeDeclare(ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);
            channel.ConfirmSelect();
            channel.BasicPublish(
                exchange: ExchangeName,
                routingKey: integrationEvent.GetType().Name,
                properties: BuildProperties(channel),
                body: body);

            var timeoutMs = Math.Max(1, _options.ConfirmTimeoutMs);
            if (!channel.WaitForConfirms(TimeSpan.FromMilliseconds(timeoutMs)))
            {
                throw new TimeoutException("RabbitMQ publish confirm timed out.");
            }

            return;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish integration event {EventType}.", integrationEvent.GetType().Name);
            throw;
        }
    }

    private static IRabbitMqBasicProperties BuildProperties(IRabbitMqChannel channel)
    {
        var props = channel.CreateBasicProperties();
            props.ContentType = "application/json";
            props.DeliveryMode = 2;
        return props;
    }
}
