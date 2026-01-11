namespace AtlasX.Api;

internal record PlaceOrderRequest(
    string symbol,
    string side,
    string type,
    decimal quantity,
    decimal? price
);

internal record OrderResponse(
    Guid orderId,
    string status,
    decimal remainingQuantity,
    IReadOnlyList<TradeResponse> trades
);

internal record TradeResponse(
    Guid id,
    decimal price,
    decimal quantity,
    Guid makerOrderId,
    Guid takerOrderId,
    DateTime executedAtUtc
);

internal record OrderBookSnapshotResponse(
    string symbol,
    IReadOnlyList<OrderBookLevelResponse> bids,
    IReadOnlyList<OrderBookLevelResponse> asks
);

internal record OrderBookLevelResponse(
    decimal price,
    decimal quantity,
    int orderCount
);

internal record DepositRequest(
    string asset,
    decimal amount
);

internal record BalanceResponse(
    string asset,
    decimal available,
    decimal reserved
);
