using System.Collections.Concurrent;
using AtlasX.Api;
using AtlasX.Infrastructure;
using AtlasX.Ledger;
using AtlasX.Matching;
using AtlasX.Risk;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Context;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, loggerConfig) =>
{
    loggerConfig
        .MinimumLevel.Information()
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(AuthConstants.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("trade", policy =>
        policy.RequireAssertion(ctx => HasScope(ctx, "trade")));
    options.AddPolicy("wallet", policy =>
        policy.RequireAssertion(ctx => HasScope(ctx, "wallet")));
});
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
builder.Services.AddSingleton<IdempotencyStore>();
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
        tracing
            .AddAspNetCoreInstrumentation()
            .AddSource(Observability.ActivitySourceName))
    .WithMetrics(metrics =>
        metrics
            .AddAspNetCoreInstrumentation()
            .AddMeter(Observability.MeterName)
            .AddPrometheusExporter());

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();

app.UseSerilogRequestLogging();
app.Use(async (context, next) =>
{
    using (LogContext.PushProperty("TraceId", Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier))
    {
        await next();
    }
});

app.UseWebSockets();

app.MapGet("/health", () => Results.Ok(new { service = "AtlasX", status = "OK" }));
app.MapPrometheusScrapingEndpoint("/metrics");

app.MapPost("/api/orders", async (
    [FromBody] PlaceOrderRequest req,
    [FromHeader(Name = "X-Client-Id")] string? clientId,
    [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
    ConcurrentDictionary<string, OrderBook> books,
    IRiskService riskService,
    ILedgerService ledgerService,
    ConcurrentDictionary<string, AccountId> accountIds,
    ConcurrentDictionary<Guid, OrderOwner> orderOwners,
    IOutbox outbox,
    MarketWebSocketManager marketWebSocketManager,
    ILogger<Program> logger,
    IdempotencyStore idempotencyStore) =>
{
    if (req is null)
    {
        return Results.Json(new { errors = new[] { "Request body is required." } }, statusCode: StatusCodes.Status400BadRequest);
    }

    var resolvedClientId = clientId?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(resolvedClientId))
    {
        return Results.Json(new { errors = new[] { "X-Client-Id header is required." } }, statusCode: StatusCodes.Status400BadRequest);
    }

    if (string.IsNullOrWhiteSpace(idempotencyKey))
    {
        return Results.Json(new { errors = new[] { "Idempotency-Key header is required." } }, statusCode: StatusCodes.Status400BadRequest);
    }

    if (idempotencyStore.TryGet(resolvedClientId, idempotencyKey, out var cached))
    {
        return Results.Json(cached.Payload, statusCode: cached.StatusCode);
    }

    var symbol = req.symbol?.Trim() ?? string.Empty;
    if (string.IsNullOrWhiteSpace(symbol))
    {
        return CacheAndReturn(
            idempotencyStore,
            resolvedClientId,
            idempotencyKey,
            StatusCodes.Status400BadRequest,
            new { errors = new[] { "Symbol must be provided." } });
    }

    if (!ApiHelpers.TryParseSide(req.side, out var side))
    {
        return CacheAndReturn(
            idempotencyStore,
            resolvedClientId,
            idempotencyKey,
            StatusCodes.Status400BadRequest,
            new { errors = new[] { "Side must be BUY or SELL." } });
    }

    if (!ApiHelpers.TryParseType(req.type, out var type))
    {
        return CacheAndReturn(
            idempotencyStore,
            resolvedClientId,
            idempotencyKey,
            StatusCodes.Status400BadRequest,
            new { errors = new[] { "Type must be LIMIT or MARKET." } });
    }

    if (req.quantity <= 0)
    {
        return CacheAndReturn(
            idempotencyStore,
            resolvedClientId,
            idempotencyKey,
            StatusCodes.Status400BadRequest,
            new { errors = new[] { "Quantity must be greater than zero." } });
    }

    if (type == OrderType.Limit)
    {
        if (req.price is null || req.price <= 0)
        {
            return CacheAndReturn(
                idempotencyStore,
                resolvedClientId,
                idempotencyKey,
                StatusCodes.Status400BadRequest,
                new { errors = new[] { "Limit orders require a price greater than zero." } });
        }
    }
    else if (type == OrderType.Market)
    {
        if (req.price is not null)
        {
            return CacheAndReturn(
                idempotencyStore,
                resolvedClientId,
                idempotencyKey,
                StatusCodes.Status400BadRequest,
                new { errors = new[] { "Market orders must not specify a price." } });
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
        return CacheAndReturn(
            idempotencyStore,
            resolvedClientId,
            idempotencyKey,
            StatusCodes.Status400BadRequest,
            new { errors = riskResult.Errors });
    }

    var accountId = accountIds.GetOrAdd(resolvedClientId, _ => new AccountId(Guid.NewGuid()));
    AssetPair assetPair;
    try
    {
        assetPair = ApiHelpers.MapAssets(symbol);
    }
    catch (ArgumentException ex)
    {
        return CacheAndReturn(
            idempotencyStore,
            resolvedClientId,
            idempotencyKey,
            StatusCodes.Status400BadRequest,
            new { errors = new[] { ex.Message } });
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
        return CacheAndReturn(
            idempotencyStore,
            resolvedClientId,
            idempotencyKey,
            StatusCodes.Status400BadRequest,
            new { errors = reservationResult.Errors });
    }

    var order = new Order(
        Guid.NewGuid(),
        symbol,
        side,
        type,
        req.quantity,
        req.price,
        DateTime.UtcNow);

    logger.LogInformation(
        "Order accepted {OrderId} {Symbol} {Side} {Type} {Quantity} {Price}",
        order.Id,
        order.Symbol,
        order.Side,
        order.Type,
        order.Quantity,
        order.Price);

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
    using var activity = Observability.ActivitySource.StartActivity("MatchOrder");
    activity?.SetTag("symbol", order.Symbol);
    activity?.SetTag("order.id", order.Id);
    activity?.SetTag("order.side", order.Side.ToString());
    activity?.SetTag("order.type", order.Type.ToString());

    var started = Stopwatch.GetTimestamp();
    var result = book.AddOrder(order);
    var elapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    Observability.OrderProcessingMs.Record(elapsedMs);
    Observability.OrdersPlaced.Add(1);

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
        Observability.TradesExecuted.Add(result.Trades.Count);
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

    var response = new OrderResponse(
        order.Id,
        status,
        order.RemainingQuantity,
        tradeResponses);

    return CacheAndReturn(
        idempotencyStore,
        resolvedClientId,
        idempotencyKey,
        StatusCodes.Status200OK,
        response);
}).RequireAuthorization("trade").WithOpenApi(operation =>
{
    operation.Parameters.Add(new OpenApiParameter
    {
        Name = "X-Client-Id",
        In = ParameterLocation.Header,
        Required = true,
        Schema = new OpenApiSchema { Type = "string" }
    });
    operation.Parameters.Add(new OpenApiParameter
    {
        Name = "Idempotency-Key",
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
}).RequireAuthorization("wallet");

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
}).RequireAuthorization("wallet");

app.Run();

static bool HasScope(AuthorizationHandlerContext context, string scope)
{
    var scopeValue = context.User.FindFirst("scope")?.Value;
    if (string.IsNullOrWhiteSpace(scopeValue))
    {
        return false;
    }

    var scopes = scopeValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return scopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
}

static IResult CacheAndReturn(
    IdempotencyStore idempotencyStore,
    string clientId,
    string key,
    int statusCode,
    object payload)
{
    idempotencyStore.Store(clientId, key, statusCode, payload);
    return Results.Json(payload, statusCode: statusCode);
}
