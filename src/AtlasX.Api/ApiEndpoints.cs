using System.Collections.Concurrent;
using System.Diagnostics;
using AtlasX.Contracts.V1;
using AtlasX.Infrastructure;
using AtlasX.Ledger;
using AtlasX.Matching;
using AtlasX.Risk;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;

namespace AtlasX.Api;

internal static class ApiEndpoints
{
    internal static void MapExchangeApi(RouteGroupBuilder group)
    {
        group.MapPost("orders", async (
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
            var validation = ApiValidation.ValidatePlaceOrder(req, clientId, idempotencyKey);
            if (!validation.IsValid)
            {
                var payload = new { errors = validation.Errors };
                if (validation.CanCache)
                {
                    return CacheAndReturn(
                        idempotencyStore,
                        validation.ClientId,
                        validation.IdempotencyKey,
                        StatusCodes.Status400BadRequest,
                        payload);
                }

                return Results.Json(payload, statusCode: StatusCodes.Status400BadRequest);
            }

            if (idempotencyStore.TryGet(validation.ClientId, validation.IdempotencyKey, out var cached))
            {
                return Results.Json(cached.Payload, statusCode: cached.StatusCode);
            }

            var riskResult = riskService.ValidatePlaceOrder(new PlaceOrderContext(
                validation.ClientId,
                validation.Symbol,
                validation.Side,
                validation.Type,
                validation.Quantity,
                validation.Price));

            if (!riskResult.IsValid)
            {
                return CacheAndReturn(
                    idempotencyStore,
                    validation.ClientId,
                    validation.IdempotencyKey,
                    StatusCodes.Status400BadRequest,
                    new { errors = riskResult.Errors });
            }

            var accountId = accountIds.GetOrAdd(validation.ClientId, _ => new AccountId(Guid.NewGuid()));
            AssetPair assetPair;
            try
            {
                assetPair = ApiHelpers.MapAssets(validation.Symbol);
            }
            catch (ArgumentException ex)
            {
                return CacheAndReturn(
                    idempotencyStore,
                    validation.ClientId,
                    validation.IdempotencyKey,
                    StatusCodes.Status400BadRequest,
                    new { errors = new[] { ex.Message } });
            }

            var reservationResult = ApiHelpers.TryReserveFunds(
                ledgerService,
                accountId,
                validation.Side,
                validation.Type,
                validation.Quantity,
                validation.Price,
                assetPair);
            if (!reservationResult.IsValid)
            {
                return CacheAndReturn(
                    idempotencyStore,
                    validation.ClientId,
                    validation.IdempotencyKey,
                    StatusCodes.Status400BadRequest,
                    new { errors = reservationResult.Errors });
            }

            var order = new Order(
                Guid.NewGuid(),
                validation.Symbol,
                validation.Side,
                validation.Type,
                validation.Quantity,
                validation.Price,
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

            orderOwners[order.Id] = new OrderOwner(accountId, validation.Side, validation.Type, validation.Price);
            var book = books.GetOrAdd(validation.Symbol, key => new OrderBook(key));
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
                riskService.UpdateLastTradePrice(validation.Symbol, lastTrade.Price);
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
                validation.ClientId,
                validation.IdempotencyKey,
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

        group.MapGet("orderbook/{symbol}", (
            string symbol,
            int? depth,
            ConcurrentDictionary<string, OrderBook> books) =>
        {
            var validation = ApiValidation.ValidateOrderBook(symbol, depth);
            if (!validation.IsValid)
            {
                return Results.Json(new { errors = validation.Errors }, statusCode: StatusCodes.Status400BadRequest);
            }

            if (!books.TryGetValue(validation.Symbol, out var book))
            {
                var emptySnapshot = new OrderBookSnapshot(validation.Symbol, Array.Empty<OrderBookLevel>(), Array.Empty<OrderBookLevel>());
                return Results.Ok(ApiHelpers.MapSnapshot(emptySnapshot));
            }

            var snapshot = book.Snapshot(validation.Depth);
            return Results.Ok(ApiHelpers.MapSnapshot(snapshot));
        }).WithOpenApi();

        group.MapPost("wallets/deposit", (
            [FromBody] DepositRequest req,
            [FromHeader(Name = "X-Client-Id")] string? clientId,
            ILedgerService ledgerService,
            ConcurrentDictionary<string, AccountId> accountIds) =>
        {
            var validation = ApiValidation.ValidateDeposit(req, clientId);
            if (!validation.IsValid)
            {
                return Results.Json(new { errors = validation.Errors }, statusCode: StatusCodes.Status400BadRequest);
            }

            var accountId = accountIds.GetOrAdd(validation.ClientId, _ => new AccountId(Guid.NewGuid()));
            ledgerService.Deposit(accountId, validation.Asset, validation.Amount);

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

        group.MapGet("wallets/balances", (
            [FromHeader(Name = "X-Client-Id")] string? clientId,
            ILedgerService ledgerService,
            ConcurrentDictionary<string, AccountId> accountIds) =>
        {
            var validation = ApiValidation.ValidateClientId(clientId);
            if (!validation.IsValid)
            {
                return Results.Json(new { errors = validation.Errors }, statusCode: StatusCodes.Status400BadRequest);
            }

            var accountId = accountIds.GetOrAdd(validation.ClientId, _ => new AccountId(Guid.NewGuid()));
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
    }

    private static IResult CacheAndReturn(
        IdempotencyStore idempotencyStore,
        string clientId,
        string key,
        int statusCode,
        object payload)
    {
        idempotencyStore.Store(clientId, key, statusCode, payload);
        return Results.Json(payload, statusCode: statusCode);
    }
}
