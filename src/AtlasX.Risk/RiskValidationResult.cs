namespace AtlasX.Risk;

/// <summary>
/// Contains the outcome of a risk validation attempt.
/// </summary>
public sealed record RiskValidationResult(bool IsValid, IReadOnlyList<string> Errors);
