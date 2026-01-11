// Invariants: Quantity > 0; Price > 0; Symbol non-empty; OrderIds are non-empty GUIDs.
namespace AtlasX.Matching;

/// <summary>
/// Represents an executed trade between a maker and a taker order.
/// </summary>
public sealed record Trade
{
    public Guid Id { get; }
    public string Symbol { get; }
    public decimal Price { get; }
    public decimal Quantity { get; }
    public Guid TakerOrderId { get; }
    public Guid MakerOrderId { get; }
    public DateTime ExecutedAtUtc { get; }

    /// <summary>
    /// Creates a new trade with validated invariants.
    /// </summary>
    public Trade(
        Guid id,
        string symbol,
        decimal price,
        decimal quantity,
        Guid takerOrderId,
        Guid makerOrderId,
        DateTime executedAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Trade Id must be a non-empty GUID.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol must be provided.", nameof(symbol));
        }

        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be greater than zero.");
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (takerOrderId == Guid.Empty)
        {
            throw new ArgumentException("TakerOrderId must be a non-empty GUID.", nameof(takerOrderId));
        }

        if (makerOrderId == Guid.Empty)
        {
            throw new ArgumentException("MakerOrderId must be a non-empty GUID.", nameof(makerOrderId));
        }

        Id = id;
        Symbol = symbol;
        Price = price;
        Quantity = quantity;
        TakerOrderId = takerOrderId;
        MakerOrderId = makerOrderId;
        ExecutedAtUtc = executedAtUtc;
    }
}
