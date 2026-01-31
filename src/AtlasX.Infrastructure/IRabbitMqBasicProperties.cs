namespace AtlasX.Infrastructure;

public interface IRabbitMqBasicProperties
{
    string? ContentType { get; set; }
    byte DeliveryMode { get; set; }
}
