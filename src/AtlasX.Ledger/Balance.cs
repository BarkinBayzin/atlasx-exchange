namespace AtlasX.Ledger;

/// <summary>
/// Represents available and reserved balances for an asset.
/// </summary>
public sealed record Balance(decimal Available, decimal Reserved);
