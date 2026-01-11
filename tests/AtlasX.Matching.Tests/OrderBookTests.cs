namespace AtlasX.Matching.Tests;

public class OrderBookTests
{
    [Fact]
    public void Buy_vs_sell_limit_produces_trade()
    {
        var book = new OrderBook("BTC-USD");
        var maker = new Order(
            Guid.NewGuid(),
            "BTC-USD",
            OrderSide.Sell,
            OrderType.Limit,
            1m,
            100m,
            DateTime.UtcNow);

        var taker = new Order(
            Guid.NewGuid(),
            "BTC-USD",
            OrderSide.Buy,
            OrderType.Limit,
            1m,
            100m,
            DateTime.UtcNow);

        book.AddOrder(maker);
        var result = book.AddOrder(taker);

        Assert.Single(result.Trades);
        var trade = result.Trades[0];
        Assert.Equal(100m, trade.Price);
        Assert.Equal(maker.Id, trade.MakerOrderId);
        Assert.Equal(taker.Id, trade.TakerOrderId);
        Assert.Null(result.RestingOrder);
    }

    [Fact]
    public void Time_priority_is_honored_for_same_price()
    {
        var book = new OrderBook("BTC-USD");
        var first = new Order(
            Guid.NewGuid(),
            "BTC-USD",
            OrderSide.Sell,
            OrderType.Limit,
            1m,
            100m,
            DateTime.UtcNow);

        var second = new Order(
            Guid.NewGuid(),
            "BTC-USD",
            OrderSide.Sell,
            OrderType.Limit,
            1m,
            100m,
            DateTime.UtcNow.AddSeconds(1));

        book.AddOrder(first);
        book.AddOrder(second);

        var taker = new Order(
            Guid.NewGuid(),
            "BTC-USD",
            OrderSide.Buy,
            OrderType.Limit,
            2m,
            100m,
            DateTime.UtcNow);

        var result = book.AddOrder(taker);

        Assert.Equal(2, result.Trades.Count);
        Assert.Equal(first.Id, result.Trades[0].MakerOrderId);
        Assert.Equal(second.Id, result.Trades[1].MakerOrderId);
    }

    [Fact]
    public void Partial_fill_consumes_across_price_levels()
    {
        var book = new OrderBook("BTC-USD");
        var cheap = new Order(
            Guid.NewGuid(),
            "BTC-USD",
            OrderSide.Sell,
            OrderType.Limit,
            1m,
            99m,
            DateTime.UtcNow);

        var expensive = new Order(
            Guid.NewGuid(),
            "BTC-USD",
            OrderSide.Sell,
            OrderType.Limit,
            1m,
            101m,
            DateTime.UtcNow);

        book.AddOrder(cheap);
        book.AddOrder(expensive);

        var taker = new Order(
            Guid.NewGuid(),
            "BTC-USD",
            OrderSide.Buy,
            OrderType.Limit,
            2m,
            101m,
            DateTime.UtcNow);

        var result = book.AddOrder(taker);

        Assert.Equal(2, result.Trades.Count);
        Assert.Equal(99m, result.Trades[0].Price);
        Assert.Equal(101m, result.Trades[1].Price);
    }

    [Fact]
    public void Market_order_consumes_liquidity_and_does_not_rest()
    {
        var book = new OrderBook("BTC-USD");
        var maker = new Order(
            Guid.NewGuid(),
            "BTC-USD",
            OrderSide.Sell,
            OrderType.Limit,
            1m,
            100m,
            DateTime.UtcNow);

        book.AddOrder(maker);

        var taker = new Order(
            Guid.NewGuid(),
            "BTC-USD",
            OrderSide.Buy,
            OrderType.Market,
            1m,
            null,
            DateTime.UtcNow);

        var result = book.AddOrder(taker);
        var snapshot = book.Snapshot();

        Assert.Single(result.Trades);
        Assert.Null(result.RestingOrder);
        Assert.Empty(snapshot.Bids);
        Assert.Empty(snapshot.Asks);
    }
}
