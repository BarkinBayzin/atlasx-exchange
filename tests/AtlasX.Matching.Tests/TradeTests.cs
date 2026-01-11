namespace AtlasX.Matching.Tests;

public class TradeTests
{
    [Fact]
    public void Trade_requires_price_greater_than_zero()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Trade(
                Guid.NewGuid(),
                "BTC-USD",
                0m,
                1m,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTime.UtcNow));

        Assert.Equal("price", ex.ParamName);
    }

    [Fact]
    public void Trade_requires_quantity_greater_than_zero()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Trade(
                Guid.NewGuid(),
                "BTC-USD",
                100m,
                0m,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTime.UtcNow));

        Assert.Equal("quantity", ex.ParamName);
    }

    [Fact]
    public void Trade_requires_symbol()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Trade(
                Guid.NewGuid(),
                " ",
                100m,
                1m,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTime.UtcNow));

        Assert.Equal("symbol", ex.ParamName);
    }

    [Fact]
    public void Trade_requires_non_empty_order_ids()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Trade(
                Guid.NewGuid(),
                "BTC-USD",
                100m,
                1m,
                Guid.Empty,
                Guid.NewGuid(),
                DateTime.UtcNow));

        Assert.Equal("takerOrderId", ex.ParamName);
    }

    [Fact]
    public void Trade_requires_non_empty_id()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Trade(
                Guid.Empty,
                "BTC-USD",
                100m,
                1m,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTime.UtcNow));

        Assert.Equal("id", ex.ParamName);
    }

    [Fact]
    public void Trade_requires_non_empty_maker_order_id()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            new Trade(
                Guid.NewGuid(),
                "BTC-USD",
                100m,
                1m,
                Guid.NewGuid(),
                Guid.Empty,
                DateTime.UtcNow));

        Assert.Equal("makerOrderId", ex.ParamName);
    }
}
