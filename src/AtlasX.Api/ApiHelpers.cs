using AtlasX.Matching;
using AtlasX.Ledger;
using AtlasX.Infrastructure;

namespace AtlasX.Api;

internal static class ApiHelpers
{
    internal static bool TryParseSide(string? value, out OrderSide side)
    {
        side = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "BUY", StringComparison.OrdinalIgnoreCase))
        {
            side = OrderSide.Buy;
            return true;
        }

        if (string.Equals(normalized, "SELL", StringComparison.OrdinalIgnoreCase))
        {
            side = OrderSide.Sell;
            return true;
        }

        return false;
    }

    internal static bool TryParseType(string? value, out OrderType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Trim();
        if (string.Equals(normalized, "LIMIT", StringComparison.OrdinalIgnoreCase))
        {
            type = OrderType.Limit;
            return true;
        }

        if (string.Equals(normalized, "MARKET", StringComparison.OrdinalIgnoreCase))
        {
            type = OrderType.Market;
            return true;
        }

        return false;
    }

    internal static string ResolveStatus(decimal originalQuantity, decimal remainingQuantity, int tradeCount)
    {
        if (remainingQuantity <= 0)
        {
            return "FILLED";
        }

        if (tradeCount > 0 && remainingQuantity < originalQuantity)
        {
            return "PARTIALLY_FILLED";
        }

        return "ACCEPTED";
    }

    internal static OrderBookSnapshotResponse MapSnapshot(OrderBookSnapshot snapshot)
    {
        var bids = snapshot.Bids.Select(level => new OrderBookLevelResponse(
            level.Price,
            level.Quantity,
            level.OrderCount)).ToList();

        var asks = snapshot.Asks.Select(level => new OrderBookLevelResponse(
            level.Price,
            level.Quantity,
            level.OrderCount)).ToList();

        return new OrderBookSnapshotResponse(snapshot.Symbol, bids, asks);
    }

    internal static AssetPair MapAssets(string symbol)
    {
        if (string.Equals(symbol, "BTC-USD", StringComparison.OrdinalIgnoreCase))
        {
            return new AssetPair("BTC", "USD");
        }

        throw new ArgumentException($"Unsupported symbol: {symbol}.", nameof(symbol));
    }

    internal static ReservationResult TryReserveFunds(
        ILedgerService ledgerService,
        AccountId accountId,
        OrderSide side,
        OrderType type,
        decimal quantity,
        decimal? price,
        AssetPair assetPair)
    {
        if (type == OrderType.Market && side == OrderSide.Buy)
        {
            return ReservationResult.Failure("Market buy requires max quote amount; not supported yet");
        }

        try
        {
            if (side == OrderSide.Buy)
            {
                if (type == OrderType.Limit)
                {
                    var quoteAmount = price.GetValueOrDefault() * quantity;
                    ledgerService.Reserve(accountId, assetPair.QuoteAsset, quoteAmount);
                }
            }
            else
            {
                ledgerService.Reserve(accountId, assetPair.BaseAsset, quantity);
            }
        }
        catch (InvalidOperationException)
        {
            var asset = side == OrderSide.Buy ? assetPair.QuoteAsset : assetPair.BaseAsset;
            return ReservationResult.Failure($"Insufficient balance for {asset}.");
        }

        return ReservationResult.Success();
    }

    internal static void SettleTrades(
        ILedgerService ledgerService,
        IReadOnlyDictionary<Guid, OrderOwner> orderOwners,
        AssetPair assetPair,
        IReadOnlyList<Trade> trades)
    {
        foreach (var trade in trades)
        {
            if (!orderOwners.TryGetValue(trade.TakerOrderId, out var takerOwner))
            {
                throw new InvalidOperationException($"Missing taker account for order {trade.TakerOrderId}.");
            }

            if (!orderOwners.TryGetValue(trade.MakerOrderId, out var makerOwner))
            {
                throw new InvalidOperationException($"Missing maker account for order {trade.MakerOrderId}.");
            }

            var buyerOwner = takerOwner.Side == OrderSide.Buy ? takerOwner : makerOwner;
            var sellerOwner = takerOwner.Side == OrderSide.Buy ? makerOwner : takerOwner;

            ApplySettlement(ledgerService, buyerOwner, sellerOwner, assetPair, trade);
        }
    }

    internal static void ReleaseUnfilledMarket(
        ILedgerService ledgerService,
        AccountId accountId,
        AssetPair assetPair,
        OrderSide side,
        decimal remainingQuantity)
    {
        if (remainingQuantity <= 0)
        {
            return;
        }

        if (side == OrderSide.Sell)
        {
            ledgerService.Release(accountId, assetPair.BaseAsset, remainingQuantity);
        }
    }

    internal static IReadOnlyList<BalanceUpdated> BuildBalanceUpdates(
        IReadOnlyDictionary<Guid, OrderOwner> orderOwners,
        AssetPair assetPair,
        IReadOnlyList<Trade> trades)
    {
        var updates = new List<BalanceUpdated>();

        foreach (var trade in trades)
        {
            if (!orderOwners.TryGetValue(trade.TakerOrderId, out var takerOwner))
            {
                throw new InvalidOperationException($"Missing taker account for order {trade.TakerOrderId}.");
            }

            if (!orderOwners.TryGetValue(trade.MakerOrderId, out var makerOwner))
            {
                throw new InvalidOperationException($"Missing maker account for order {trade.MakerOrderId}.");
            }

            var buyerOwner = takerOwner.Side == OrderSide.Buy ? takerOwner : makerOwner;
            var sellerOwner = takerOwner.Side == OrderSide.Buy ? makerOwner : takerOwner;
            var notional = trade.Price * trade.Quantity;
            var occurredAt = DateTime.UtcNow;

            updates.Add(new BalanceUpdated(
                Guid.NewGuid(),
                occurredAt,
                buyerOwner.AccountId.Value,
                assetPair.QuoteAsset,
                0m,
                -notional));
            updates.Add(new BalanceUpdated(
                Guid.NewGuid(),
                occurredAt,
                buyerOwner.AccountId.Value,
                assetPair.BaseAsset,
                trade.Quantity,
                0m));
            updates.Add(new BalanceUpdated(
                Guid.NewGuid(),
                occurredAt,
                sellerOwner.AccountId.Value,
                assetPair.BaseAsset,
                0m,
                -trade.Quantity));
            updates.Add(new BalanceUpdated(
                Guid.NewGuid(),
                occurredAt,
                sellerOwner.AccountId.Value,
                assetPair.QuoteAsset,
                notional,
                0m));

            if (buyerOwner.Type == OrderType.Limit && buyerOwner.Price is not null)
            {
                var priceDiff = buyerOwner.Price.Value - trade.Price;
                if (priceDiff > 0)
                {
                    updates.Add(new BalanceUpdated(
                        Guid.NewGuid(),
                        occurredAt,
                        buyerOwner.AccountId.Value,
                        assetPair.QuoteAsset,
                        priceDiff * trade.Quantity,
                        -priceDiff * trade.Quantity));
                }
            }
        }

        return updates;
    }

    private static void ApplySettlement(
        ILedgerService ledgerService,
        OrderOwner buyerOwner,
        OrderOwner sellerOwner,
        AssetPair assetPair,
        Trade trade)
    {
        var notional = trade.Price * trade.Quantity;
        ledgerService.Release(buyerOwner.AccountId, assetPair.QuoteAsset, notional);
        ledgerService.Debit(buyerOwner.AccountId, assetPair.QuoteAsset, notional);
        ledgerService.Credit(buyerOwner.AccountId, assetPair.BaseAsset, trade.Quantity);
        ledgerService.Release(sellerOwner.AccountId, assetPair.BaseAsset, trade.Quantity);
        ledgerService.Debit(sellerOwner.AccountId, assetPair.BaseAsset, trade.Quantity);
        ledgerService.Credit(sellerOwner.AccountId, assetPair.QuoteAsset, notional);

        if (buyerOwner.Type == OrderType.Limit && buyerOwner.Price is not null)
        {
            var priceDiff = buyerOwner.Price.Value - trade.Price;
            if (priceDiff > 0)
            {
                ledgerService.Release(buyerOwner.AccountId, assetPair.QuoteAsset, priceDiff * trade.Quantity);
            }
        }
    }
}

internal readonly record struct AssetPair(string BaseAsset, string QuoteAsset);

internal sealed record ReservationResult(bool IsValid, IReadOnlyList<string> Errors)
{
    internal static ReservationResult Success() => new(true, Array.Empty<string>());

    internal static ReservationResult Failure(string error)
        => new(false, new[] { error });
}

internal readonly record struct OrderOwner(AccountId AccountId, OrderSide Side, OrderType Type, decimal? Price);
