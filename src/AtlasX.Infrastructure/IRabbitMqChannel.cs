namespace AtlasX.Infrastructure;

public interface IRabbitMqChannel : IDisposable
{
    void ExchangeDeclare(string exchange, string type, bool durable, bool autoDelete);
    void ConfirmSelect();
    IRabbitMqBasicProperties CreateBasicProperties();
    void BasicPublish(string exchange, string routingKey, IRabbitMqBasicProperties properties, ReadOnlyMemory<byte> body);
    bool WaitForConfirms(TimeSpan timeout);
}
