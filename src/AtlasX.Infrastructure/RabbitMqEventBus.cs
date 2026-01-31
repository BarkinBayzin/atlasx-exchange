using System.Text;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace AtlasX.Infrastructure;

/// <summary>
/// Publishes integration events to RabbitMQ.
/// </summary>
public sealed class RabbitMqEventBus : IEventBus
{
    private const string ExchangeName = "atlasx.events";
    private readonly ILogger<RabbitMqEventBus> _logger;

    /// <summary>
    /// Initializes a new instance of the event bus.
    /// </summary>
    public RabbitMqEventBus(ILogger<RabbitMqEventBus> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (integrationEvent is null)
        {
            throw new ArgumentNullException(nameof(integrationEvent));
        }

        try
        {
            var factory = new ConnectionFactory
            {
                HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost"
            };

            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            channel.ExchangeDeclare(ExchangeName, ExchangeType.Topic, durable: true, autoDelete: false);

            var payload = IntegrationEventSerializer.Serialize(integrationEvent);
            var body = Encoding.UTF8.GetBytes(payload);
            var props = channel.CreateBasicProperties();
            props.ContentType = "application/json";
            props.DeliveryMode = 2;

            channel.BasicPublish(
                exchange: ExchangeName,
                routingKey: integrationEvent.GetType().Name,
                basicProperties: props,
                body: body);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish integration event {EventType}.", integrationEvent.GetType().Name);
            throw;
        }
    }
}
