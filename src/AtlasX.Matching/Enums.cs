// Invariants: OrderSide and OrderType are constrained to defined enum values.
namespace AtlasX.Matching;

/// <summary>
/// Defines whether an order is a buy or sell.
/// </summary>
public enum OrderSide
{
    Buy = 0,
    Sell = 1
}

/// <summary>
/// Defines whether an order is a limit or market order.
/// </summary>
public enum OrderType
{
    Limit = 0,
    Market = 1
}
