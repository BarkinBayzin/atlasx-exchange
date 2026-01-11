namespace AtlasX.Infrastructure;

/// <summary>
/// Represents an integration event for external publication.
/// </summary>
public interface IIntegrationEvent
{
    /// <summary>
    /// Gets the event identifier.
    /// </summary>
    Guid EventId { get; }

    /// <summary>
    /// Gets the UTC time when the event occurred.
    /// </summary>
    DateTime OccurredAtUtc { get; }
}
