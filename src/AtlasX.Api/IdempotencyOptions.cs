namespace AtlasX.Api;

internal sealed class IdempotencyOptions
{
    internal const int DefaultTtlSecondsFallback = 86_400;
    internal const int DefaultMaxKeysTotalFallback = 10_000;
    internal const int DefaultMaxKeysPerClientFallback = 1_000;

    public int DefaultTtlSeconds { get; set; } = DefaultTtlSecondsFallback;

    public int MaxKeysTotal { get; set; } = DefaultMaxKeysTotalFallback;

    public int MaxKeysPerClient { get; set; } = DefaultMaxKeysPerClientFallback;
}
