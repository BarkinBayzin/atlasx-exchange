namespace AtlasX.Infrastructure;

public sealed class RabbitMqOptions
{
    internal const int DefaultConfirmTimeoutMs = 5000;
    internal const int DefaultMaxChannels = 4;
    internal const int DefaultReconnectBackoffMs = 2000;

    public string HostName { get; set; } = "localhost";

    public int ConfirmTimeoutMs { get; set; } = DefaultConfirmTimeoutMs;

    public int MaxChannels { get; set; } = DefaultMaxChannels;

    public int ReconnectBackoffMs { get; set; } = DefaultReconnectBackoffMs;
}
