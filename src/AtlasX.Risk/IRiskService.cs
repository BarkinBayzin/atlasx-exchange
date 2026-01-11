namespace AtlasX.Risk;

/// <summary>
/// Defines risk checks for order placement.
/// </summary>
public interface IRiskService
{
    /// <summary>
    /// Validates a new order placement request.
    /// </summary>
    RiskValidationResult ValidatePlaceOrder(PlaceOrderContext context);

    /// <summary>
    /// Updates the last trade price for a symbol.
    /// </summary>
    void UpdateLastTradePrice(string symbol, decimal price);
}
