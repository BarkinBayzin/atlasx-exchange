namespace AtlasX.Api;

internal sealed class OutboxOptions
{
    internal const int DefaultPollIntervalMs = 1000;
    internal const int DefaultBatchSize = 100;
    internal const int DefaultLeaseSeconds = 30;
    internal const int DefaultMaxAttempts = 5;
    internal const int DefaultBaseRetryDelayMs = 500;
    internal const int DefaultMaxRetryDelayMs = 30_000;
    internal const int DefaultMaxParallelism = 4;

    public int PollIntervalMs { get; set; } = DefaultPollIntervalMs;

    public int BatchSize { get; set; } = DefaultBatchSize;

    public int LeaseSeconds { get; set; } = DefaultLeaseSeconds;

    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    public int BaseRetryDelayMs { get; set; } = DefaultBaseRetryDelayMs;

    public int MaxRetryDelayMs { get; set; } = DefaultMaxRetryDelayMs;

    public int MaxParallelism { get; set; } = DefaultMaxParallelism;
}
