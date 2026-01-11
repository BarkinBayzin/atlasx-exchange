namespace AtlasX.Infrastructure;

/// <summary>
/// Indicates an order has been accepted for processing.
/// </summary>
public sealed record OrderAccepted(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid OrderId,
    string Symbol,
    string Side,
    string Type,
    decimal Quantity,
    decimal? Price) : IIntegrationEvent;

/// <summary>
/// Indicates an order produced one or more matches.
/// </summary>
public sealed record OrderMatched(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid OrderId,
    string Symbol,
    int TradeCount) : IIntegrationEvent;

/// <summary>
/// Indicates a trade has been settled.
/// </summary>
public sealed record TradeSettled(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid TradeId,
    string Symbol,
    decimal Price,
    decimal Quantity,
    Guid MakerOrderId,
    Guid TakerOrderId) : IIntegrationEvent;

/// <summary>
/// Indicates a balance change for an account and asset.
/// </summary>
public sealed record BalanceUpdated(
    Guid EventId,
    DateTime OccurredAtUtc,
    Guid AccountId,
    string Asset,
    decimal AvailableDelta,
    decimal ReservedDelta) : IIntegrationEvent;
