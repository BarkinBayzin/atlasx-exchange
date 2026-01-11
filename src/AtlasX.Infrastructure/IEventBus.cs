namespace AtlasX.Infrastructure;

/// <summary>
/// Publishes integration events to external systems.
/// </summary>
public interface IEventBus
{
    /// <summary>
    /// Publishes an integration event.
    /// </summary>
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken);
}
