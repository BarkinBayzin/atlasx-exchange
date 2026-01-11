namespace AtlasX.Ledger;

/// <summary>
/// Defines ledger operations for balances and reservations.
/// </summary>
public interface ILedgerService
{
    /// <summary>
    /// Deposits funds into an account's available balance.
    /// </summary>
    void Deposit(AccountId accountId, string asset, decimal amount);

    /// <summary>
    /// Reserves funds from an account's available balance.
    /// </summary>
    void Reserve(AccountId accountId, string asset, decimal amount);

    /// <summary>
    /// Releases funds from reserved back to available balance.
    /// </summary>
    void Release(AccountId accountId, string asset, decimal amount);

    /// <summary>
    /// Credits funds directly to available balance.
    /// </summary>
    void Credit(AccountId accountId, string asset, decimal amount);

    /// <summary>
    /// Debits funds directly from available balance.
    /// </summary>
    void Debit(AccountId accountId, string asset, decimal amount);

    /// <summary>
    /// Gets balances for all assets in the account.
    /// </summary>
    IReadOnlyDictionary<string, Balance> GetBalances(AccountId accountId);
}
