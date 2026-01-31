namespace AtlasX.Infrastructure;

public interface IRabbitMqConnectionManager
{
    Task<IRabbitMqChannel> RentChannelAsync(CancellationToken cancellationToken);
}
