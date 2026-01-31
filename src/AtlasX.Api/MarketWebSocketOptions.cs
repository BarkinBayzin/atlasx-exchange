namespace AtlasX.Api;

internal sealed class MarketWebSocketOptions
{
    internal const int DefaultBatchWindowMs = 150;
    internal const int DefaultMaxMessagesPerSecond = 20;

    public int BatchWindowMs { get; set; } = DefaultBatchWindowMs;

    public int MaxMessagesPerSecond { get; set; } = DefaultMaxMessagesPerSecond;
}
