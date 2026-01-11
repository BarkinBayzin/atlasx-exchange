using Microsoft.Extensions.Hosting;

namespace AtlasX.Api;

internal sealed class MarketWebSocketHeartbeatService : BackgroundService
{
    private readonly MarketWebSocketManager _manager;

    public MarketWebSocketHeartbeatService(MarketWebSocketManager manager)
    {
        _manager = manager ?? throw new ArgumentNullException(nameof(manager));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await _manager.SendHeartbeatAsync();
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
