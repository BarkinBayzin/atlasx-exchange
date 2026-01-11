// Invariants: Id non-empty; Symbol non-empty; Quantity > 0; Limit orders require Price > 0; Market orders require Price null; RemainingQuantity starts at Quantity.
namespace AtlasX.Matching;

/// <summary>
/// Represents a single order submitted to the matching engine.
/// </summary>
public sealed record Order
{
    public Guid Id { get; }
    public string Symbol { get; }
    public OrderSide Side { get; }
    public OrderType Type { get; }
    public decimal Quantity { get; }
    public decimal RemainingQuantity { get; private set; }
    public decimal? Price { get; }
    public DateTime CreatedAtUtc { get; }

    /// <summary>
    /// Creates a new order with validated invariants.
    /// </summary>
    public Order(
        Guid id,
        string symbol,
        OrderSide side,
        OrderType type,
        decimal quantity,
        decimal? price,
        DateTime createdAtUtc)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Order Id must be a non-empty GUID.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol must be provided.", nameof(symbol));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (type == OrderType.Limit)
        {
            if (price is null || price <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(price), "Limit orders require a price greater than zero.");
            }
        }
        else if (type == OrderType.Market)
        {
            if (price is not null)
            {
                throw new ArgumentException("Market orders must not specify a price.", nameof(price));
            }
        }

        Id = id;
        Symbol = symbol;
        Side = side;
        Type = type;
        Quantity = quantity;
        RemainingQuantity = quantity;
        Price = price;
        CreatedAtUtc = createdAtUtc;
    }

    internal void DecreaseRemaining(decimal quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Fill quantity must be greater than zero.");
        }

        if (quantity > RemainingQuantity)
        {
            throw new InvalidOperationException("Fill quantity exceeds remaining quantity.");
        }

        RemainingQuantity -= quantity;
    }
}
