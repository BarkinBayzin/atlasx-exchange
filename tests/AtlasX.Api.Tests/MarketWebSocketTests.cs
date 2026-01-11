using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AtlasX.Api.Tests;

public class MarketWebSocketTests
{
    [Fact]
    public async Task Websocket_sends_initial_snapshot()
    {
        using var factory = new WebApplicationFactory<Program>();
        var client = factory.Server.CreateWebSocketClient();
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/ws/market?symbol=BTC-USD"), CancellationToken.None);

        var message = await ReceiveAsync(socket, TimeSpan.FromSeconds(2));

        Assert.NotNull(message);
        Assert.Equal("snapshot", message!.type);
        Assert.Equal("BTC-USD", message.symbol);
    }

    private static async Task<MarketMessage?> ReceiveAsync(WebSocket socket, TimeSpan timeout)
    {
        var buffer = new byte[4096];
        using var cts = new CancellationTokenSource(timeout);
        var result = await socket.ReceiveAsync(buffer, cts.Token);
        if (result.MessageType == WebSocketMessageType.Close)
        {
            return null;
        }

        var payload = Encoding.UTF8.GetString(buffer, 0, result.Count);
        return JsonSerializer.Deserialize<MarketMessage>(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private sealed record MarketMessage(
        string type,
        string symbol,
        JsonElement snapshot,
        JsonElement? trade,
        DateTime? timestampUtc);
}
