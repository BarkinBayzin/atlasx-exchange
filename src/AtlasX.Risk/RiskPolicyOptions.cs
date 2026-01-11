namespace AtlasX.Risk;

/// <summary>
/// Configures risk limits for order placement.
/// </summary>
public sealed record RiskPolicyOptions
{
    /// <summary>
    /// Gets or sets the maximum quantity allowed per order.
    /// </summary>
    public decimal MaxQuantityPerOrder { get; init; }

    /// <summary>
    /// Gets or sets the maximum allowed price deviation percentage from last trade.
    /// </summary>
    public decimal PriceBandPercent { get; init; }

    /// <summary>
    /// Gets or sets the maximum number of requests per client per minute.
    /// </summary>
    public int RequestsPerMinutePerClient { get; init; }
}
