namespace AtlasX.Matching.Tests;

public class OrderTests
{
    [Fact]
    public void LimitOrder_requires_price_greater_than_zero()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Order(
                Guid.NewGuid(),
                "BTC-USD",
                OrderSide.Buy,
                OrderType.Limit,
                1m,
                0m,
                DateTime.UtcNow));

        Assert.Equal("price", ex.ParamName);
    }

    [Fact]
    public void LimitOrder_requires_price_to_be_present()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Order(
                Guid.NewGuid(),
                "BTC-USD",
                OrderSide.Buy,
                OrderType.Limit,
                1m,
                null,
                DateTime.UtcNow));

        Assert.Equal("price", ex.ParamName);
    }

    [Fact]
    public void MarketOrder_requires_price_to_be_null()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Order(
                Guid.NewGuid(),
                "BTC-USD",
                OrderSide.Buy,
                OrderType.Market,
                1m,
                100m,
                DateTime.UtcNow));

        Assert.Equal("price", ex.ParamName);
    }

    [Fact]
    public void Order_requires_quantity_greater_than_zero()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Order(
                Guid.NewGuid(),
                "BTC-USD",
                OrderSide.Buy,
                OrderType.Limit,
                0m,
                100m,
                DateTime.UtcNow));

        Assert.Equal("quantity", ex.ParamName);
    }

    [Fact]
    public void Order_initializes_remaining_quantity_to_quantity()
    {
        var order = new Order(
            Guid.NewGuid(),
            "BTC-USD",
            OrderSide.Buy,
            OrderType.Limit,
            5m,
            101m,
            DateTime.UtcNow);

        Assert.Equal(5m, order.RemainingQuantity);
    }

    [Fact]
    public void Order_requires_non_empty_id()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Order(
                Guid.Empty,
                "BTC-USD",
                OrderSide.Buy,
                OrderType.Limit,
                1m,
                100m,
                DateTime.UtcNow));

        Assert.Equal("id", ex.ParamName);
    }

    [Fact]
    public void Order_requires_symbol()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Order(
                Guid.NewGuid(),
                " ",
                OrderSide.Buy,
                OrderType.Limit,
                1m,
                100m,
                DateTime.UtcNow));

        Assert.Equal("symbol", ex.ParamName);
    }
}
