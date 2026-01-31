using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AtlasX.Api;
using AtlasX.Matching;
using Microsoft.Extensions.Options;

namespace AtlasX.Api.Tests;

public class MarketWebSocketBatchingTests
{
    [Fact]
    public async Task Trades_are_batched_within_window()
    {
        var manager = CreateManager(batchWindowMs: 50, maxMessagesPerSecond: 1000);
        var socket = new TestWebSocket();
        manager.AddClient("BTC-USD", socket);

        var firstBatch = BuildTrades(3);
        var secondBatch = BuildTrades(3);

        await manager.BroadcastTradesAsync("BTC-USD", firstBatch);
        await manager.BroadcastTradesAsync("BTC-USD", secondBatch);

        await Task.Delay(120);

        var messages = socket.Messages;
        Assert.Single(messages);

        var message = Parse(messages[0]);
        Assert.Equal("trades", message.Type);
        Assert.Equal(6, message.Trades!.Length);
    }

    [Fact]
    public async Task Trade_order_is_deterministic_in_batch()
    {
        var manager = CreateManager(batchWindowMs: 50, maxMessagesPerSecond: 1000);
        var socket = new TestWebSocket();
        manager.AddClient("BTC-USD", socket);

        var trades = BuildTrades(3).ToList();
        await manager.BroadcastTradesAsync("BTC-USD", trades);
        await Task.Delay(120);

        var message = Parse(socket.Messages.Single());
        Assert.Equal("trades", message.Type);
        Assert.Equal(trades[0].Id, message.Trades![0].GetProperty("id").GetGuid());
        Assert.Equal(trades[1].Id, message.Trades![1].GetProperty("id").GetGuid());
        Assert.Equal(trades[2].Id, message.Trades![2].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Snapshot_is_not_throttled()
    {
        var manager = CreateManager(batchWindowMs: 50, maxMessagesPerSecond: 1);
        var socket = new TestWebSocket();
        var connectionId = manager.AddClient("BTC-USD", socket);

        var snapshot = new global::AtlasX.Contracts.V1.OrderBookSnapshotResponse(
            "BTC-USD",
            Array.Empty<global::AtlasX.Contracts.V1.OrderBookLevelResponse>(),
            Array.Empty<global::AtlasX.Contracts.V1.OrderBookLevelResponse>());

        await manager.SendSnapshotAsync(connectionId, "BTC-USD", snapshot);

        var trades = BuildTrades(5);
        await manager.BroadcastTradesAsync("BTC-USD", trades);
        await Task.Delay(120);

        Assert.True(socket.Messages.Count >= 1);
        var first = Parse(socket.Messages[0]);
        Assert.Equal("snapshot", first.Type);
    }

    private static MarketWebSocketManager CreateManager(int batchWindowMs, int maxMessagesPerSecond)
    {
        var options = Options.Create(new MarketWebSocketOptions
        {
            BatchWindowMs = batchWindowMs,
            MaxMessagesPerSecond = maxMessagesPerSecond
        });
        return new MarketWebSocketManager(options);
    }

    private static IReadOnlyList<Trade> BuildTrades(int count)
    {
        var list = new List<Trade>();
        for (var i = 0; i < count; i++)
        {
            list.Add(new Trade(Guid.NewGuid(), "BTC-USD", 100m + i, 1m, Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow));
        }

        return list;
    }

    private static ParsedMessage Parse(string payload)
    {
        using var doc = JsonDocument.Parse(payload);
        var root = doc.RootElement;
        var type = root.GetProperty("type").GetString();
        var trades = root.TryGetProperty("trades", out var tradesElement) && tradesElement.ValueKind == JsonValueKind.Array
            ? tradesElement.EnumerateArray().Select(element => element.Clone()).ToArray()
            : Array.Empty<JsonElement>();
        return new ParsedMessage(type!, trades);
    }

    private sealed record ParsedMessage(string Type, JsonElement[] Trades);

    private sealed class TestWebSocket : WebSocket
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyList<string> Messages => _messages.ToList();

        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;

        public override void Abort()
        {
        }

        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override void Dispose()
        {
        }

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            => Task.FromResult(new WebSocketReceiveResult(0, WebSocketMessageType.Text, true));

        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            var payload = Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count);
            _messages.Enqueue(payload);
            return Task.CompletedTask;
        }
    }
}
