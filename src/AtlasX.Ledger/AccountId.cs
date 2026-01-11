namespace AtlasX.Ledger;

/// <summary>
/// Identifies a ledger account.
/// </summary>
public readonly record struct AccountId
{
    /// <summary>
    /// Gets the underlying identifier value.
    /// </summary>
    public Guid Value { get; }

    /// <summary>
    /// Creates an account identifier from a non-empty GUID.
    /// </summary>
    public AccountId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("AccountId must be a non-empty GUID.", nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// Returns the identifier as a string.
    /// </summary>
    public override string ToString() => Value.ToString();
}
