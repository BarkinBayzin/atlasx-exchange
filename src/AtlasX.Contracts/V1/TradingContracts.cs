namespace AtlasX.Contracts.V1;

public sealed record PlaceOrderRequest(
    string Symbol,
    string Side,
    string Type,
    decimal Quantity,
    decimal? Price
);

public sealed record OrderResponse(
    Guid OrderId,
    string Status,
    decimal RemainingQuantity,
    IReadOnlyList<TradeResponse> Trades
);

public sealed record TradeResponse(
    Guid Id,
    decimal Price,
    decimal Quantity,
    Guid MakerOrderId,
    Guid TakerOrderId,
    DateTime ExecutedAtUtc
);

public sealed record OrderBookSnapshotResponse(
    string Symbol,
    IReadOnlyList<OrderBookLevelResponse> Bids,
    IReadOnlyList<OrderBookLevelResponse> Asks
);

public sealed record OrderBookLevelResponse(
    decimal Price,
    decimal Quantity,
    int OrderCount
);

public sealed record DepositRequest(
    string Asset,
    decimal Amount
);

public sealed record BalanceResponse(
    string Asset,
    decimal Available,
    decimal Reserved
);
