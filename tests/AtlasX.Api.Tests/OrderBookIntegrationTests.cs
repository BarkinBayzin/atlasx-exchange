using System.Net.Http.Json;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AtlasX.Api.Tests;

public class OrderBookIntegrationTests
{
    [Fact]
    public async Task Place_sell_then_buy_returns_trade_and_filled_status()
    {
        using var factory = new WebApplicationFactory<Program>();
        var sellerClient = factory.CreateClient();
        sellerClient.DefaultRequestHeaders.Add("X-Client-Id", "seller-1");
        ApplyAuth(sellerClient, "trade", "wallet");

        var buyerClient = factory.CreateClient();
        buyerClient.DefaultRequestHeaders.Add("X-Client-Id", "buyer-1");
        ApplyAuth(buyerClient, "trade", "wallet");

        var sellerDeposit = await sellerClient.PostAsJsonAsync("/api/wallets/deposit", new DepositRequest("BTC", 1m));
        sellerDeposit.EnsureSuccessStatusCode();

        var buyerDeposit = await buyerClient.PostAsJsonAsync("/api/wallets/deposit", new DepositRequest("USD", 100m));
        buyerDeposit.EnsureSuccessStatusCode();

        var sell = new PlaceOrderRequest("BTC-USD", "SELL", "LIMIT", 1m, 100m);
        var sellResponse = await PostOrderAsync(sellerClient, sell, Guid.NewGuid().ToString());
        sellResponse.EnsureSuccessStatusCode();

        var buy = new PlaceOrderRequest("BTC-USD", "BUY", "LIMIT", 1m, 100m);
        var buyResponse = await PostOrderAsync(buyerClient, buy, Guid.NewGuid().ToString());
        buyResponse.EnsureSuccessStatusCode();

        var buyResult = await buyResponse.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.NotNull(buyResult);
        Assert.Equal("FILLED", buyResult!.status);
        Assert.Single(buyResult.trades);
        Assert.Equal(100m, buyResult.trades[0].price);
    }

    [Fact]
    public async Task Orderbook_snapshot_reflects_post_trade_state()
    {
        using var factory = new WebApplicationFactory<Program>();
        var sellerClient = factory.CreateClient();
        sellerClient.DefaultRequestHeaders.Add("X-Client-Id", "seller-1");
        ApplyAuth(sellerClient, "trade", "wallet");

        var buyerClient = factory.CreateClient();
        buyerClient.DefaultRequestHeaders.Add("X-Client-Id", "buyer-1");
        ApplyAuth(buyerClient, "trade", "wallet");

        var sellerDeposit = await sellerClient.PostAsJsonAsync("/api/wallets/deposit", new DepositRequest("BTC", 2m));
        sellerDeposit.EnsureSuccessStatusCode();

        var buyerDeposit = await buyerClient.PostAsJsonAsync("/api/wallets/deposit", new DepositRequest("USD", 100m));
        buyerDeposit.EnsureSuccessStatusCode();

        var sell = new PlaceOrderRequest("BTC-USD", "SELL", "LIMIT", 2m, 100m);
        var sellResponse = await PostOrderAsync(sellerClient, sell, Guid.NewGuid().ToString());
        sellResponse.EnsureSuccessStatusCode();

        var buy = new PlaceOrderRequest("BTC-USD", "BUY", "LIMIT", 1m, 100m);
        var buyResponse = await PostOrderAsync(buyerClient, buy, Guid.NewGuid().ToString());
        buyResponse.EnsureSuccessStatusCode();

        var snapshotResponse = await buyerClient.GetAsync("/api/orderbook/BTC-USD?depth=10");
        snapshotResponse.EnsureSuccessStatusCode();

        var snapshot = await snapshotResponse.Content.ReadFromJsonAsync<OrderBookSnapshotResponse>();

        Assert.NotNull(snapshot);
        Assert.Empty(snapshot!.bids);
        Assert.Single(snapshot.asks);
        Assert.Equal(100m, snapshot.asks[0].price);
        Assert.Equal(1m, snapshot.asks[0].quantity);
        Assert.Equal(1, snapshot.asks[0].orderCount);
    }

    [Fact]
    public async Task Missing_client_id_header_returns_400()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        ApplyAuth(client, "trade");

        var order = new PlaceOrderRequest("BTC-USD", "BUY", "LIMIT", 1m, 100m);
        var response = await PostOrderAsync(client, order, Guid.NewGuid().ToString());

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Exceeding_max_quantity_returns_400()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Client-Id", "client-1");
        ApplyAuth(client, "trade");

        var order = new PlaceOrderRequest("BTC-USD", "BUY", "LIMIT", 101m, 100m);
        var response = await PostOrderAsync(client, order, Guid.NewGuid().ToString());

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Limit_order_outside_price_band_is_rejected()
    {
        using var factory = new WebApplicationFactory<Program>();
        var sellerClient = factory.CreateClient();
        sellerClient.DefaultRequestHeaders.Add("X-Client-Id", "seller-1");
        ApplyAuth(sellerClient, "trade", "wallet");

        var buyerClient = factory.CreateClient();
        buyerClient.DefaultRequestHeaders.Add("X-Client-Id", "buyer-1");
        ApplyAuth(buyerClient, "trade", "wallet");

        var sellerDeposit = await sellerClient.PostAsJsonAsync("/api/wallets/deposit", new DepositRequest("BTC", 1m));
        sellerDeposit.EnsureSuccessStatusCode();

        var buyerDeposit = await buyerClient.PostAsJsonAsync("/api/wallets/deposit", new DepositRequest("USD", 100m));
        buyerDeposit.EnsureSuccessStatusCode();

        var sell = new PlaceOrderRequest("BTC-USD", "SELL", "LIMIT", 1m, 100m);
        var sellResponse = await PostOrderAsync(sellerClient, sell, Guid.NewGuid().ToString());
        sellResponse.EnsureSuccessStatusCode();

        var buy = new PlaceOrderRequest("BTC-USD", "BUY", "LIMIT", 1m, 100m);
        var buyResponse = await PostOrderAsync(buyerClient, buy, Guid.NewGuid().ToString());
        buyResponse.EnsureSuccessStatusCode();

        var outOfBand = new PlaceOrderRequest("BTC-USD", "BUY", "LIMIT", 1m, 200m);
        var outOfBandResponse = await PostOrderAsync(buyerClient, outOfBand, Guid.NewGuid().ToString());

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, outOfBandResponse.StatusCode);
    }

    [Fact]
    public async Task Deposit_usd_then_buy_limit_reserves_or_rejects_based_on_balance()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Client-Id", "buyer-1");
        ApplyAuth(client, "trade", "wallet");

        var firstDeposit = await client.PostAsJsonAsync("/api/wallets/deposit", new DepositRequest("USD", 50m));
        firstDeposit.EnsureSuccessStatusCode();

        var insufficient = new PlaceOrderRequest("BTC-USD", "BUY", "LIMIT", 1m, 100m);
        var insufficientResponse = await PostOrderAsync(client, insufficient, Guid.NewGuid().ToString());
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, insufficientResponse.StatusCode);

        var secondDeposit = await client.PostAsJsonAsync("/api/wallets/deposit", new DepositRequest("USD", 100m));
        secondDeposit.EnsureSuccessStatusCode();

        var sufficient = new PlaceOrderRequest("BTC-USD", "BUY", "LIMIT", 1m, 100m);
        var sufficientResponse = await PostOrderAsync(client, sufficient, Guid.NewGuid().ToString());
        sufficientResponse.EnsureSuccessStatusCode();

        var balancesResponse = await client.GetAsync("/api/wallets/balances");
        balancesResponse.EnsureSuccessStatusCode();
        var balances = await balancesResponse.Content.ReadFromJsonAsync<List<BalanceResponse>>();

        Assert.NotNull(balances);
        var usd = Assert.Single(balances!, b => b.asset == "USD");
        Assert.Equal(50m, usd.available);
        Assert.Equal(100m, usd.reserved);
    }

    [Fact]
    public async Task Sell_limit_trade_settles_balances_for_buyer_and_seller()
    {
        using var factory = new WebApplicationFactory<Program>();
        var sellerClient = factory.CreateClient();
        sellerClient.DefaultRequestHeaders.Add("X-Client-Id", "seller-1");
        ApplyAuth(sellerClient, "trade", "wallet");

        var buyerClient = factory.CreateClient();
        buyerClient.DefaultRequestHeaders.Add("X-Client-Id", "buyer-1");
        ApplyAuth(buyerClient, "trade", "wallet");

        var sellerDeposit = await sellerClient.PostAsJsonAsync("/api/wallets/deposit", new DepositRequest("BTC", 1m));
        sellerDeposit.EnsureSuccessStatusCode();

        var buyerDeposit = await buyerClient.PostAsJsonAsync("/api/wallets/deposit", new DepositRequest("USD", 100m));
        buyerDeposit.EnsureSuccessStatusCode();

        var sell = new PlaceOrderRequest("BTC-USD", "SELL", "LIMIT", 1m, 100m);
        var sellResponse = await PostOrderAsync(sellerClient, sell, Guid.NewGuid().ToString());
        sellResponse.EnsureSuccessStatusCode();

        var buy = new PlaceOrderRequest("BTC-USD", "BUY", "LIMIT", 1m, 100m);
        var buyResponse = await PostOrderAsync(buyerClient, buy, Guid.NewGuid().ToString());
        buyResponse.EnsureSuccessStatusCode();

        var sellerBalancesResponse = await sellerClient.GetAsync("/api/wallets/balances");
        sellerBalancesResponse.EnsureSuccessStatusCode();
        var sellerBalances = await sellerBalancesResponse.Content.ReadFromJsonAsync<List<BalanceResponse>>();

        var buyerBalancesResponse = await buyerClient.GetAsync("/api/wallets/balances");
        buyerBalancesResponse.EnsureSuccessStatusCode();
        var buyerBalances = await buyerBalancesResponse.Content.ReadFromJsonAsync<List<BalanceResponse>>();

        Assert.NotNull(sellerBalances);
        Assert.NotNull(buyerBalances);

        var sellerBtc = Assert.Single(sellerBalances!, b => b.asset == "BTC");
        var sellerUsd = Assert.Single(sellerBalances, b => b.asset == "USD");
        Assert.Equal(0m, sellerBtc.available);
        Assert.Equal(0m, sellerBtc.reserved);
        Assert.Equal(100m, sellerUsd.available);

        var buyerBtc = Assert.Single(buyerBalances!, b => b.asset == "BTC");
        var buyerUsd = Assert.Single(buyerBalances, b => b.asset == "USD");
        Assert.Equal(1m, buyerBtc.available);
        Assert.Equal(0m, buyerBtc.reserved);
        Assert.Equal(0m, buyerUsd.available);
        Assert.Equal(0m, buyerUsd.reserved);
    }

    [Fact]
    public async Task Market_buy_is_rejected()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Client-Id", "buyer-1");
        ApplyAuth(client, "trade");

        var buy = new PlaceOrderRequest("BTC-USD", "BUY", "MARKET", 1m, null);
        var response = await PostOrderAsync(client, buy, Guid.NewGuid().ToString());

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Orders_require_authentication()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Client-Id", "client-1");
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var order = new PlaceOrderRequest("BTC-USD", "BUY", "LIMIT", 1m, 100m);
        var response = await client.PostAsJsonAsync("/api/orders", order);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wallets_require_authentication()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Client-Id", "client-1");

        var response = await client.GetAsync("/api/wallets/balances");

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Idempotency_returns_same_response_on_retry()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Client-Id", "buyer-1");
        ApplyAuth(client, "trade", "wallet");

        var deposit = await client.PostAsJsonAsync("/api/wallets/deposit", new DepositRequest("USD", 100m));
        deposit.EnsureSuccessStatusCode();

        var order = new PlaceOrderRequest("BTC-USD", "BUY", "LIMIT", 1m, 100m);
        var key = Guid.NewGuid().ToString();

        var first = await PostOrderAsync(client, order, key);
        first.EnsureSuccessStatusCode();
        var firstResult = await first.Content.ReadFromJsonAsync<OrderResponse>();

        var second = await PostOrderAsync(client, order, key);
        second.EnsureSuccessStatusCode();
        var secondResult = await second.Content.ReadFromJsonAsync<OrderResponse>();

        Assert.NotNull(firstResult);
        Assert.NotNull(secondResult);
        Assert.Equal(firstResult!.orderId, secondResult!.orderId);
        Assert.Equal(firstResult.remainingQuantity, secondResult.remainingQuantity);
        Assert.Equal(firstResult.status, secondResult.status);
    }

    private static void ApplyAuth(HttpClient client, params string[] scopes)
    {
        var token = TestAuthToken.CreateToken(scopes);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static Task<HttpResponseMessage> PostOrderAsync(HttpClient client, PlaceOrderRequest request, string idempotencyKey)
    {
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", idempotencyKey);
        return client.PostAsJsonAsync("/api/orders", request);
    }
}

internal record PlaceOrderRequest(string symbol, string side, string type, decimal quantity, decimal? price);

internal record OrderResponse(Guid orderId, string status, decimal remainingQuantity, List<TradeResponse> trades);

internal record TradeResponse(Guid id, decimal price, decimal quantity, Guid makerOrderId, Guid takerOrderId, DateTime executedAtUtc);

internal record OrderBookSnapshotResponse(string symbol, List<OrderBookLevelResponse> bids, List<OrderBookLevelResponse> asks);

internal record OrderBookLevelResponse(decimal price, decimal quantity, int orderCount);

internal record DepositRequest(string asset, decimal amount);

internal record BalanceResponse(string asset, decimal available, decimal reserved);
