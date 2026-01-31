using System.Collections.Concurrent;
using System.Diagnostics;
using AtlasX.Api;
using AtlasX.Infrastructure;
using AtlasX.Ledger;
using AtlasX.Matching;
using AtlasX.Risk;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
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
builder.Services.AddOptions<RabbitMqOptions>()
    .Bind(builder.Configuration.GetSection("RabbitMQ"));
builder.Services.AddSingleton<IRabbitMqConnectionManager>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<RabbitMqOptions>>().Value;
    var logger = sp.GetRequiredService<ILogger<RabbitMqConnectionManager>>();
    return new RabbitMqConnectionManager(options, logger);
});
builder.Services.AddSingleton<IEventBus, RabbitMqEventBus>();
builder.Services.AddOptions<OutboxOptions>()
    .Bind(builder.Configuration.GetSection("Outbox"));
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddOptions<MarketWebSocketOptions>()
    .Bind(builder.Configuration.GetSection("WebSocket"));
builder.Services.AddSingleton<MarketWebSocketManager>();
builder.Services.AddHostedService<MarketWebSocketHeartbeatService>();
builder.Services.AddOptions<IdempotencyOptions>()
    .Bind(builder.Configuration.GetSection("Idempotency"));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<IdempotencyOptions>>().Value;
    return new IdempotencyStore(options);
});
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

var apiGroup = app.MapGroup("/api");
ApiEndpoints.MapExchangeApi(apiGroup);

var apiV1Group = app.MapGroup("/api/v1");
ApiEndpoints.MapExchangeApi(apiV1Group);

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
