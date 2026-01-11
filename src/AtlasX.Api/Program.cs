using System.Collections.Concurrent;
using AtlasX.Api;
using AtlasX.Infrastructure;
using AtlasX.Ledger;
using AtlasX.Matching;
using AtlasX.Risk;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(new ConcurrentDictionary<string, OrderBook>(StringComparer.Ordinal));
builder.Services.AddSingleton(new RiskPolicyOptions
{
    MaxQuantityPerOrder = 100m,
    PriceBandPercent = 10m,
    RequestsPerMinutePerClient = 60
});
builder.Services.AddSingleton<IRiskService, RiskService>();
builder.Services.AddSingleton<ILedgerService, LedgerService>();
builder.Services.AddSingleton(new ConcurrentDictionary<string, AccountId>(StringComparer.Ordinal));
builder.Services.AddSingleton(new ConcurrentDictionary<Guid, OrderOwner>());
builder.Services.AddSingleton<IOutbox, InMemoryOutbox>();
builder.Services.AddSingleton<IEventBus, RabbitMqEventBus>();
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddSingleton<MarketWebSocketManager>();
builder.Services.AddHostedService<MarketWebSocketHeartbeatService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseWebSockets();

app.MapGet("/health", () => Results.Ok(new { service = "AtlasX", status = "OK" }));

app.MapPost("/api/orders", async (
    [FromBody] PlaceOrderRequest req,
    [FromHeader(Name = "X-Client-Id")] string? clientId,
    ConcurrentDictionary<string, OrderBook> books,
    IRiskService riskService,
    ILedgerService ledgerService,
    ConcurrentDictionary<string, AccountId> accountIds,
    ConcurrentDictionary<Guid, OrderOwner> orderOwners,
    IOutbox outbox,
    MarketWebSocketManager marketWebSocketManager) =>
{
    if (req is null)
    {
        return Results.BadRequest(new { errors = new[] { "Request body is required." } });
    }

    var resolvedClientId = clientId?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(resolvedClientId))
    {
        return Results.BadRequest(new { errors = new[] { "X-Client-Id header is required." } });
    }

    var symbol = req.symbol?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(symbol))
    {
        return Results.BadRequest(new { errors = new[] { "Symbol must be provided." } });
    }

    if (!ApiHelpers.TryParseSide(req.side, out var side))
    {
        return Results.BadRequest(new { errors = new[] { "Side must be BUY or SELL." } });
    }

    if (!ApiHelpers.TryParseType(req.type, out var type))
    {
        return Results.BadRequest(new { errors = new[] { "Type must be LIMIT or MARKET." } });
    }

    if (req.quantity <= 0)
    {
        return Results.BadRequest(new { errors = new[] { "Quantity must be greater than zero." } });
    }

    if (type == OrderType.Limit)
    {
        if (req.price is null || req.price <= 0)
        {
            return Results.BadRequest(new { errors = new[] { "Limit orders require a price greater than zero." } });
        }
    }
    else if (type == OrderType.Market)
    {
        if (req.price is not null)
        {
            return Results.BadRequest(new { errors = new[] { "Market orders must not specify a price." } });
        }
    }

    var riskResult = riskService.ValidatePlaceOrder(new PlaceOrderContext(
        resolvedClientId,
        symbol,
        side,
        type,
        req.quantity,
        req.price));

    if (!riskResult.IsValid)
    {
        return Results.BadRequest(new { errors = riskResult.Errors });
    }

    var accountId = accountIds.GetOrAdd(resolvedClientId, _ => new AccountId(Guid.NewGuid()));
    AssetPair assetPair;
    try
    {
        assetPair = ApiHelpers.MapAssets(symbol);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { errors = new[] { ex.Message } });
    }

    var reservationResult = ApiHelpers.TryReserveFunds(
        ledgerService,
        accountId,
        side,
        type,
        req.quantity,
        req.price,
        assetPair);
    if (!reservationResult.IsValid)
    {
        return Results.BadRequest(new { errors = reservationResult.Errors });
    }

    var order = new Order(
        Guid.NewGuid(),
        symbol,
        side,
        type,
        req.quantity,
        req.price,
        DateTime.UtcNow);

    outbox.Enqueue(new OrderAccepted(
        Guid.NewGuid(),
        DateTime.UtcNow,
        order.Id,
        order.Symbol,
        order.Side.ToString(),
        order.Type.ToString(),
        order.Quantity,
        order.Price));

    orderOwners[order.Id] = new OrderOwner(accountId, side, type, req.price);
    var book = books.GetOrAdd(symbol, key => new OrderBook(key));
    var result = book.AddOrder(order);

    var status = ApiHelpers.ResolveStatus(order.Quantity, order.RemainingQuantity, result.Trades.Count);
    var tradeResponses = result.Trades.Select(trade => new TradeResponse(
        trade.Id,
        trade.Price,
        trade.Quantity,
        trade.MakerOrderId,
        trade.TakerOrderId,
        trade.ExecutedAtUtc)).ToList();

    var snapshot = book.Snapshot(10);
    await marketWebSocketManager.BroadcastOrderBookAsync(order.Symbol, ApiHelpers.MapSnapshot(snapshot));

    if (result.Trades.Count > 0)
    {
        outbox.Enqueue(new OrderMatched(
            Guid.NewGuid(),
            DateTime.UtcNow,
            order.Id,
            order.Symbol,
            result.Trades.Count));

        foreach (var trade in result.Trades)
        {
            outbox.Enqueue(new TradeSettled(
                Guid.NewGuid(),
                DateTime.UtcNow,
                trade.Id,
                trade.Symbol,
                trade.Price,
                trade.Quantity,
                trade.MakerOrderId,
                trade.TakerOrderId));
        }

        foreach (var balanceEvent in ApiHelpers.BuildBalanceUpdates(orderOwners, assetPair, result.Trades))
        {
            outbox.Enqueue(balanceEvent);
        }

        await marketWebSocketManager.BroadcastTradesAsync(order.Symbol, result.Trades);

        ApiHelpers.SettleTrades(ledgerService, orderOwners, assetPair, result.Trades);
        var lastTrade = result.Trades[^1];
        riskService.UpdateLastTradePrice(symbol, lastTrade.Price);
    }

    if (order.Type == OrderType.Market && order.RemainingQuantity > 0)
    {
        ApiHelpers.ReleaseUnfilledMarket(
            ledgerService,
            accountId,
            assetPair,
            order.Side,
            order.RemainingQuantity);
    }

    return Results.Ok(new OrderResponse(
        order.Id,
        status,
        order.RemainingQuantity,
        tradeResponses));
}).WithOpenApi(operation =>
{
    operation.Parameters.Add(new OpenApiParameter
    {
        Name = "X-Client-Id",
        In = ParameterLocation.Header,
        Required = true,
        Schema = new OpenApiSchema { Type = "string" }
    });
    return operation;
});

app.MapGet("/api/orderbook/{symbol}", (
    string symbol,
    int? depth,
    ConcurrentDictionary<string, OrderBook> books) =>
{
    var trimmed = symbol?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(trimmed))
    {
        return Results.BadRequest(new { errors = new[] { "Symbol must be provided." } });
    }

    var resolvedDepth = depth ?? 10;
    if (resolvedDepth <= 0)
    {
        return Results.BadRequest(new { errors = new[] { "Depth must be greater than zero." } });
    }

    if (!books.TryGetValue(trimmed, out var book))
    {
        var emptySnapshot = new OrderBookSnapshot(trimmed, Array.Empty<OrderBookLevel>(), Array.Empty<OrderBookLevel>());
        return Results.Ok(ApiHelpers.MapSnapshot(emptySnapshot));
    }

    var snapshot = book.Snapshot(resolvedDepth);
    return Results.Ok(ApiHelpers.MapSnapshot(snapshot));
}).WithOpenApi();

app.MapGet("/ws/market", async (
    HttpContext context,
    ConcurrentDictionary<string, OrderBook> books,
    MarketWebSocketManager marketWebSocketManager) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var symbol = context.Request.Query["symbol"].ToString().Trim();
    if (string.IsNullOrWhiteSpace(symbol))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var depthValue = context.Request.Query["depth"].ToString();
    var depth = 10;
    if (!string.IsNullOrWhiteSpace(depthValue) && int.TryParse(depthValue, out var parsedDepth))
    {
        depth = parsedDepth;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var connectionId = marketWebSocketManager.AddClient(symbol, socket);
    try
    {
        if (!books.TryGetValue(symbol, out var book))
        {
            var emptySnapshot = new OrderBookSnapshot(symbol, Array.Empty<OrderBookLevel>(), Array.Empty<OrderBookLevel>());
            await marketWebSocketManager.SendSnapshotAsync(connectionId, symbol, ApiHelpers.MapSnapshot(emptySnapshot));
        }
        else
        {
            var snapshot = book.Snapshot(depth);
            await marketWebSocketManager.SendSnapshotAsync(connectionId, symbol, ApiHelpers.MapSnapshot(snapshot));
        }

        var buffer = new byte[1024];
        while (socket.State == WebSocketState.Open)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }
        }
    }
    finally
    {
        marketWebSocketManager.RemoveClient(symbol, connectionId);
    }
});

app.MapPost("/api/wallets/deposit", (
    [FromBody] DepositRequest req,
    [FromHeader(Name = "X-Client-Id")] string? clientId,
    ILedgerService ledgerService,
    ConcurrentDictionary<string, AccountId> accountIds) =>
{
    if (req is null)
    {
        return Results.BadRequest(new { errors = new[] { "Request body is required." } });
    }

    var resolvedClientId = clientId?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(resolvedClientId))
    {
        return Results.BadRequest(new { errors = new[] { "X-Client-Id header is required." } });
    }

    if (string.IsNullOrWhiteSpace(req.asset))
    {
        return Results.BadRequest(new { errors = new[] { "Asset must be provided." } });
    }

    if (req.amount <= 0)
    {
        return Results.BadRequest(new { errors = new[] { "Amount must be greater than zero." } });
    }

    var accountId = accountIds.GetOrAdd(resolvedClientId, _ => new AccountId(Guid.NewGuid()));
    ledgerService.Deposit(accountId, req.asset, req.amount);

    return Results.Ok();
}).WithOpenApi(operation =>
{
    operation.Parameters.Add(new OpenApiParameter
    {
        Name = "X-Client-Id",
        In = ParameterLocation.Header,
        Required = true,
        Schema = new OpenApiSchema { Type = "string" }
    });
    return operation;
});

app.MapGet("/api/wallets/balances", (
    [FromHeader(Name = "X-Client-Id")] string? clientId,
    ILedgerService ledgerService,
    ConcurrentDictionary<string, AccountId> accountIds) =>
{
    var resolvedClientId = clientId?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(resolvedClientId))
    {
        return Results.BadRequest(new { errors = new[] { "X-Client-Id header is required." } });
    }

    var accountId = accountIds.GetOrAdd(resolvedClientId, _ => new AccountId(Guid.NewGuid()));
    var balances = ledgerService.GetBalances(accountId);
    var response = balances.Select(entry => new BalanceResponse(
        entry.Key,
        entry.Value.Available,
        entry.Value.Reserved)).ToList();

    return Results.Ok(response);
}).WithOpenApi(operation =>
{
    operation.Parameters.Add(new OpenApiParameter
    {
        Name = "X-Client-Id",
        In = ParameterLocation.Header,
        Required = true,
        Schema = new OpenApiSchema { Type = "string" }
    });
    return operation;
});

app.Run();
