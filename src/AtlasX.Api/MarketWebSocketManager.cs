using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using AtlasX.Contracts.V1;
using AtlasX.Matching;

namespace AtlasX.Api;

internal sealed class MarketWebSocketManager
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, WebSocket>> _clients =
        new(StringComparer.OrdinalIgnoreCase);

    internal Guid AddClient(string symbol, WebSocket socket)
    {
        var connections = _clients.GetOrAdd(symbol, _ => new ConcurrentDictionary<Guid, WebSocket>());
        var id = Guid.NewGuid();
        connections[id] = socket;
        return id;
    }

    internal void RemoveClient(string symbol, Guid connectionId)
    {
        if (_clients.TryGetValue(symbol, out var connections))
        {
            connections.TryRemove(connectionId, out _);
            if (connections.IsEmpty)
            {
                _clients.TryRemove(symbol, out _);
            }
        }
    }

    internal async Task SendSnapshotAsync(Guid connectionId, string symbol, OrderBookSnapshotResponse snapshot)
    {
        if (!_clients.TryGetValue(symbol, out var connections))
        {
            return;
        }

        if (!connections.TryGetValue(connectionId, out var socket))
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new MarketMessage("snapshot", symbol, snapshot, null, null), JsonOptions);
        await SendAsync(socket, payload);
    }

    internal async Task BroadcastOrderBookAsync(string symbol, OrderBookSnapshotResponse snapshot)
    {
        if (!_clients.TryGetValue(symbol, out var connections))
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new MarketMessage("orderbook", symbol, snapshot, null, null), JsonOptions);
        await BroadcastAsync(connections, payload);
    }

    internal async Task BroadcastTradesAsync(string symbol, IReadOnlyList<Trade> trades)
    {
        if (trades.Count == 0)
        {
            return;
        }

        if (!_clients.TryGetValue(symbol, out var connections))
        {
            return;
        }

        foreach (var trade in trades)
        {
            var tradePayload = new TradeMessage(
                trade.Id,
                trade.Price,
                trade.Quantity,
                trade.MakerOrderId,
                trade.TakerOrderId,
                trade.ExecutedAtUtc);

            var payload = JsonSerializer.Serialize(new MarketMessage("trade", symbol, null, tradePayload, null), JsonOptions);
            await BroadcastAsync(connections, payload);
        }
    }

    internal async Task SendHeartbeatAsync()
    {
        foreach (var kvp in _clients)
        {
            var symbol = kvp.Key;
            var connections = kvp.Value;
            var payload = JsonSerializer.Serialize(new MarketMessage("ping", symbol, null, null, DateTime.UtcNow), JsonOptions);
            await BroadcastAsync(connections, payload);
        }
    }

    private static async Task BroadcastAsync(ConcurrentDictionary<Guid, WebSocket> connections, string payload)
    {
        var failed = new List<Guid>();

        foreach (var connection in connections)
        {
            var socket = connection.Value;
            if (socket.State != WebSocketState.Open)
            {
                failed.Add(connection.Key);
                continue;
            }

            try
            {
                await SendAsync(socket, payload);
            }
            catch (Exception)
            {
                failed.Add(connection.Key);
            }
        }

        foreach (var id in failed)
        {
            connections.TryRemove(id, out _);
        }
    }

    private static Task SendAsync(WebSocket socket, string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var segment = new ArraySegment<byte>(bytes);
        return socket.SendAsync(segment, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
    }

    private sealed record MarketMessage(
        string type,
        string symbol,
        OrderBookSnapshotResponse? snapshot,
        TradeMessage? trade,
        DateTime? timestampUtc);

    private sealed record TradeMessage(
        Guid id,
        decimal price,
        decimal quantity,
        Guid makerOrderId,
        Guid takerOrderId,
        DateTime executedAtUtc);
}
