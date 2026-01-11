using System.Collections.Concurrent;
using AtlasX.Matching;

namespace AtlasX.Risk;

/// <summary>
/// Provides in-memory risk checks for order placement.
/// </summary>
public sealed class RiskService : IRiskService
{
    private readonly RiskPolicyOptions _options;
    private readonly ConcurrentDictionary<string, decimal> _lastTradePrices;
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _requestWindows;

    /// <summary>
    /// Initializes a new instance with the provided risk policy options.
    /// </summary>
    public RiskService(RiskPolicyOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _lastTradePrices = new ConcurrentDictionary<string, decimal>(StringComparer.Ordinal);
        _requestWindows = new ConcurrentDictionary<string, Queue<DateTime>>(StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public RiskValidationResult ValidatePlaceOrder(PlaceOrderContext context)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(context.ClientId))
        {
            errors.Add("ClientId is required.");
        }

        if (_options.MaxQuantityPerOrder > 0 && context.Quantity > _options.MaxQuantityPerOrder)
        {
            errors.Add("Order quantity exceeds maximum allowed.");
        }

        if (context.Type == OrderType.Limit)
        {
            if (context.Price is null || context.Price <= 0)
            {
                errors.Add("Limit orders require a price greater than zero for risk checks.");
            }
            else if (_options.PriceBandPercent > 0 &&
                     _lastTradePrices.TryGetValue(context.Symbol, out var lastPrice) &&
                     lastPrice > 0)
            {
                var deviation = Math.Abs(context.Price.Value - lastPrice) / lastPrice * 100m;
                if (deviation > _options.PriceBandPercent)
                {
                    errors.Add("Order price is outside the allowed price band.");
                }
            }
        }

        if (_options.RequestsPerMinutePerClient > 0 && !string.IsNullOrWhiteSpace(context.ClientId))
        {
            var window = _requestWindows.GetOrAdd(context.ClientId, _ => new Queue<DateTime>());
            var now = DateTime.UtcNow;
            var cutoff = now.AddMinutes(-1);

            lock (window)
            {
                while (window.Count > 0 && window.Peek() < cutoff)
                {
                    window.Dequeue();
                }

                if (window.Count >= _options.RequestsPerMinutePerClient)
                {
                    errors.Add("Rate limit exceeded.");
                }
                else
                {
                    window.Enqueue(now);
                }
            }
        }

        return new RiskValidationResult(errors.Count == 0, errors);
    }

    /// <inheritdoc />
    public void UpdateLastTradePrice(string symbol, decimal price)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol must be provided.", nameof(symbol));
        }

        if (price <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be greater than zero.");
        }

        _lastTradePrices[symbol] = price;
    }
}
