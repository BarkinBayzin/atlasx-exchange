using System.Collections.Concurrent;

namespace AtlasX.Ledger;

/// <summary>
/// Provides an in-memory ledger with available and reserved balances.
/// </summary>
public sealed class LedgerService : ILedgerService
{
    private readonly ConcurrentDictionary<AccountId, AccountBalances> _accounts = new();

    /// <inheritdoc />
    public void Deposit(AccountId accountId, string asset, decimal amount)
    {
        ValidateAmount(amount);
        var balances = GetAccountBalances(accountId);
        balances.Apply(asset, amount, 0m);
    }

    /// <inheritdoc />
    public void Reserve(AccountId accountId, string asset, decimal amount)
    {
        ValidateAmount(amount);
        var balances = GetAccountBalances(accountId);
        balances.Apply(asset, -amount, amount);
    }

    /// <inheritdoc />
    public void Release(AccountId accountId, string asset, decimal amount)
    {
        ValidateAmount(amount);
        var balances = GetAccountBalances(accountId);
        balances.Apply(asset, amount, -amount);
    }

    /// <inheritdoc />
    public void Credit(AccountId accountId, string asset, decimal amount)
    {
        ValidateAmount(amount);
        var balances = GetAccountBalances(accountId);
        balances.Apply(asset, amount, 0m);
    }

    /// <inheritdoc />
    public void Debit(AccountId accountId, string asset, decimal amount)
    {
        ValidateAmount(amount);
        var balances = GetAccountBalances(accountId);
        balances.Apply(asset, -amount, 0m);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, Balance> GetBalances(AccountId accountId)
    {
        var balances = GetAccountBalances(accountId);
        return balances.Snapshot();
    }

    private static void ValidateAmount(decimal amount)
    {
        if (amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be greater than zero.");
        }
    }

    private AccountBalances GetAccountBalances(AccountId accountId)
    {
        return _accounts.GetOrAdd(accountId, _ => new AccountBalances());
    }

    private sealed class AccountBalances
    {
        private readonly Dictionary<string, Balance> _balances = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public void Apply(string asset, decimal availableDelta, decimal reservedDelta)
        {
            if (string.IsNullOrWhiteSpace(asset))
            {
                throw new ArgumentException("Asset must be provided.", nameof(asset));
            }

            lock (_lock)
            {
                _balances.TryGetValue(asset, out var existing);
                var available = existing?.Available ?? 0m;
                var reserved = existing?.Reserved ?? 0m;

                available += availableDelta;
                reserved += reservedDelta;

                if (available < 0 || reserved < 0)
                {
                    throw new InvalidOperationException("Insufficient balance.");
                }

                _balances[asset] = new Balance(available, reserved);
            }
        }

        public IReadOnlyDictionary<string, Balance> Snapshot()
        {
            lock (_lock)
            {
                return new Dictionary<string, Balance>(_balances, StringComparer.OrdinalIgnoreCase);
            }
        }
    }
}
