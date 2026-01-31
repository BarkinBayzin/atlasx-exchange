using AtlasX.Contracts.V1;
using AtlasX.Matching;

namespace AtlasX.Api;

internal static class ApiValidation
{
    internal static PlaceOrderValidationResult ValidatePlaceOrder(
        PlaceOrderRequest? request,
        string? clientId,
        string? idempotencyKey)
    {
        if (request is null)
        {
            return PlaceOrderValidationResult.Invalid(
                canCache: false,
                errors: new[] { "Request body is required." });
        }

        var resolvedClientId = clientId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(resolvedClientId))
        {
            return PlaceOrderValidationResult.Invalid(
                canCache: false,
                errors: new[] { "X-Client-Id header is required." });
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return PlaceOrderValidationResult.Invalid(
                canCache: false,
                errors: new[] { "Idempotency-Key header is required." });
        }

        var symbol = request.Symbol?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return PlaceOrderValidationResult.Invalid(
                canCache: true,
                errors: new[] { "Symbol must be provided." });
        }

        if (!ApiHelpers.TryParseSide(request.Side, out var side))
        {
            return PlaceOrderValidationResult.Invalid(
                canCache: true,
                errors: new[] { "Side must be BUY or SELL." });
        }

        if (!ApiHelpers.TryParseType(request.Type, out var type))
        {
            return PlaceOrderValidationResult.Invalid(
                canCache: true,
                errors: new[] { "Type must be LIMIT or MARKET." });
        }

        if (request.Quantity <= 0)
        {
            return PlaceOrderValidationResult.Invalid(
                canCache: true,
                errors: new[] { "Quantity must be greater than zero." });
        }

        if (type == OrderType.Limit)
        {
            if (request.Price is null || request.Price <= 0)
            {
                return PlaceOrderValidationResult.Invalid(
                    canCache: true,
                    errors: new[] { "Limit orders require a price greater than zero." });
            }
        }
        else if (type == OrderType.Market)
        {
            if (request.Price is not null)
            {
                return PlaceOrderValidationResult.Invalid(
                    canCache: true,
                    errors: new[] { "Market orders must not specify a price." });
            }
        }

        return new PlaceOrderValidationResult(
            true,
            true,
            resolvedClientId,
            idempotencyKey,
            symbol,
            side,
            type,
            request.Quantity,
            request.Price,
            Array.Empty<string>());
    }

    internal static DepositValidationResult ValidateDeposit(DepositRequest? request, string? clientId)
    {
        if (request is null)
        {
            return DepositValidationResult.Invalid(new[] { "Request body is required." });
        }

        var resolvedClientId = clientId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(resolvedClientId))
        {
            return DepositValidationResult.Invalid(new[] { "X-Client-Id header is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Asset))
        {
            return DepositValidationResult.Invalid(new[] { "Asset must be provided." });
        }

        if (request.Amount <= 0)
        {
            return DepositValidationResult.Invalid(new[] { "Amount must be greater than zero." });
        }

        return new DepositValidationResult(
            true,
            resolvedClientId,
            request.Asset,
            request.Amount,
            Array.Empty<string>());
    }

    internal static OrderBookValidationResult ValidateOrderBook(string? symbol, int? depth)
    {
        var trimmed = symbol?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return OrderBookValidationResult.Invalid(new[] { "Symbol must be provided." });
        }

        var resolvedDepth = depth ?? 10;
        if (resolvedDepth <= 0)
        {
            return OrderBookValidationResult.Invalid(new[] { "Depth must be greater than zero." });
        }

        return new OrderBookValidationResult(true, trimmed, resolvedDepth, Array.Empty<string>());
    }

    internal static ClientValidationResult ValidateClientId(string? clientId)
    {
        var resolvedClientId = clientId?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(resolvedClientId))
        {
            return ClientValidationResult.Invalid(new[] { "X-Client-Id header is required." });
        }

        return new ClientValidationResult(true, resolvedClientId, Array.Empty<string>());
    }
}

internal readonly record struct PlaceOrderValidationResult(
    bool IsValid,
    bool CanCache,
    string ClientId,
    string IdempotencyKey,
    string Symbol,
    OrderSide Side,
    OrderType Type,
    decimal Quantity,
    decimal? Price,
    IReadOnlyList<string> Errors)
{
    internal static PlaceOrderValidationResult Invalid(bool canCache, IReadOnlyList<string> errors)
        => new(false, canCache, string.Empty, string.Empty, string.Empty, default, default, 0m, null, errors);
}

internal readonly record struct DepositValidationResult(
    bool IsValid,
    string ClientId,
    string Asset,
    decimal Amount,
    IReadOnlyList<string> Errors)
{
    internal static DepositValidationResult Invalid(IReadOnlyList<string> errors)
        => new(false, string.Empty, string.Empty, 0m, errors);
}

internal readonly record struct OrderBookValidationResult(
    bool IsValid,
    string Symbol,
    int Depth,
    IReadOnlyList<string> Errors)
{
    internal static OrderBookValidationResult Invalid(IReadOnlyList<string> errors)
        => new(false, string.Empty, 0, errors);
}

internal readonly record struct ClientValidationResult(
    bool IsValid,
    string ClientId,
    IReadOnlyList<string> Errors)
{
    internal static ClientValidationResult Invalid(IReadOnlyList<string> errors)
        => new(false, string.Empty, errors);
}
