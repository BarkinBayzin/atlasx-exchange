using AtlasX.Matching;

namespace AtlasX.Risk;

/// <summary>
/// Describes an order placement request for risk evaluation.
/// </summary>
public sealed record PlaceOrderContext(
    string ClientId,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? Price);
