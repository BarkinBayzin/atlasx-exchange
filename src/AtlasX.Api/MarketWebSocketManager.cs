using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Linq;
using System.Text;
using System.Text.Json;
using AtlasX.Contracts.V1;
using AtlasX.Matching;
using Microsoft.Extensions.Options;

namespace AtlasX.Api;

internal sealed class MarketWebSocketManager
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const int SendTimeoutMs = 1000;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, ConnectionState>> _clients =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SymbolBuffer> _buffers = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _batchWindow;
    private readonly int _maxMessagesPerSecond;

    public MarketWebSocketManager(IOptions<MarketWebSocketOptions> options)
    {
        var resolved = options?.Value ?? throw new ArgumentNullException(nameof(options));
        var windowMs = resolved.BatchWindowMs > 0 ? resolved.BatchWindowMs : MarketWebSocketOptions.DefaultBatchWindowMs;
        _batchWindow = TimeSpan.FromMilliseconds(windowMs);
        _maxMessagesPerSecond = resolved.MaxMessagesPerSecond > 0
            ? resolved.MaxMessagesPerSecond
            : MarketWebSocketOptions.DefaultMaxMessagesPerSecond;
    }

    internal Guid AddClient(string symbol, WebSocket socket)
    {
        var connections = _clients.GetOrAdd(symbol, _ => new ConcurrentDictionary<Guid, ConnectionState>());
        var id = Guid.NewGuid();
        connections[id] = new ConnectionState(socket);
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

        if (!connections.TryGetValue(connectionId, out var state))
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new MarketMessage("snapshot", symbol, snapshot, null, null, null), JsonOptions);
        await SendAsync(state, payload, isMandatory: true);
    }

    internal async Task BroadcastOrderBookAsync(string symbol, OrderBookSnapshotResponse snapshot)
    {
        if (!_clients.ContainsKey(symbol))
        {
            return;
        }

        var buffer = _buffers.GetOrAdd(symbol, _ => new SymbolBuffer());
        ScheduleFlush(buffer, symbol, snapshot, null);
    }

    internal async Task BroadcastTradesAsync(string symbol, IReadOnlyList<Trade> trades)
    {
        if (trades.Count == 0)
        {
            return;
        }

        if (!_clients.ContainsKey(symbol))
        {
            return;
        }

        var buffer = _buffers.GetOrAdd(symbol, _ => new SymbolBuffer());
        var tradePayloads = trades.Select(trade => new TradeMessage(
            trade.Id,
            trade.Price,
            trade.Quantity,
            trade.MakerOrderId,
            trade.TakerOrderId,
            trade.ExecutedAtUtc)).ToList();

        ScheduleFlush(buffer, symbol, null, tradePayloads);
    }

    internal async Task SendHeartbeatAsync()
    {
        foreach (var kvp in _clients)
        {
            var symbol = kvp.Key;
            var connections = kvp.Value;
            var payload = JsonSerializer.Serialize(new MarketMessage("ping", symbol, null, null, null, DateTime.UtcNow), JsonOptions);
            await BroadcastAsync(connections, payload, isMandatory: false);
        }
    }

    private void ScheduleFlush(SymbolBuffer buffer, string symbol, OrderBookSnapshotResponse? snapshot, List<TradeMessage>? trades)
    {
        var shouldSchedule = false;
        lock (buffer.Gate)
        {
            if (snapshot is not null)
            {
                buffer.Snapshot = snapshot;
            }

            if (trades is not null && trades.Count > 0)
            {
                buffer.Trades.AddRange(trades);
            }

            if (!buffer.FlushScheduled)
            {
                buffer.FlushScheduled = true;
                shouldSchedule = true;
            }
        }

        if (shouldSchedule)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(_batchWindow).ConfigureAwait(false);
                await FlushSymbolAsync(symbol).ConfigureAwait(false);
            });
        }
    }

    private async Task FlushSymbolAsync(string symbol)
    {
        if (!_buffers.TryGetValue(symbol, out var buffer))
        {
            return;
        }

        OrderBookSnapshotResponse? snapshot;
        List<TradeMessage> trades;
        lock (buffer.Gate)
        {
            snapshot = buffer.Snapshot;
            trades = buffer.Trades.Count > 0 ? new List<TradeMessage>(buffer.Trades) : new List<TradeMessage>();
            buffer.Snapshot = null;
            buffer.Trades.Clear();
            buffer.FlushScheduled = false;
        }

        if (!_clients.TryGetValue(symbol, out var connections))
        {
            return;
        }

        if (snapshot is not null)
        {
            var payload = JsonSerializer.Serialize(new MarketMessage("orderbook", symbol, snapshot, null, null, null), JsonOptions);
            await BroadcastAsync(connections, payload, isMandatory: false);
        }

        if (trades.Count == 1)
        {
            var payload = JsonSerializer.Serialize(new MarketMessage("trade", symbol, null, trades[0], null, null), JsonOptions);
            await BroadcastAsync(connections, payload, isMandatory: false);
        }
        else if (trades.Count > 1)
        {
            var payload = JsonSerializer.Serialize(new MarketMessage("trades", symbol, null, null, trades, null), JsonOptions);
            await BroadcastAsync(connections, payload, isMandatory: false);
        }
    }

    private async Task BroadcastAsync(ConcurrentDictionary<Guid, ConnectionState> connections, string payload, bool isMandatory)
    {
        var failed = new ConcurrentBag<Guid>();
        var tasks = new List<Task>();

        foreach (var connection in connections)
        {
            var state = connection.Value;
            if (state.Socket.State != WebSocketState.Open)
            {
                failed.Add(connection.Key);
                continue;
            }

            if (!isMandatory)
            {
                if (!state.RateLimiter.TryConsume(DateTimeOffset.UtcNow, _maxMessagesPerSecond))
                {
                    continue;
                }
            }

            tasks.Add(SendAsync(state, payload, isMandatory).ContinueWith(task =>
            {
                if (task.IsFaulted || task.IsCanceled)
                {
                    failed.Add(connection.Key);
                }
            }));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        foreach (var id in failed)
        {
            connections.TryRemove(id, out _);
        }
    }

    private static async Task SendAsync(ConnectionState state, string payload, bool isMandatory)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var segment = new ArraySegment<byte>(bytes);
        using var cts = new CancellationTokenSource(SendTimeoutMs);
        await state.Socket.SendAsync(segment, WebSocketMessageType.Text, endOfMessage: true, cts.Token);
    }

    private sealed record MarketMessage(
        string type,
        string symbol,
        OrderBookSnapshotResponse? snapshot,
        TradeMessage? trade,
        IReadOnlyList<TradeMessage>? trades,
        DateTime? timestampUtc);

    private sealed record TradeMessage(
        Guid id,
        decimal price,
        decimal quantity,
        Guid makerOrderId,
        Guid takerOrderId,
        DateTime executedAtUtc);

    private sealed class SymbolBuffer
    {
        internal readonly object Gate = new();
        internal OrderBookSnapshotResponse? Snapshot;
        internal readonly List<TradeMessage> Trades = new();
        internal bool FlushScheduled;
    }

    private sealed class ConnectionState
    {
        internal ConnectionState(WebSocket socket)
        {
            Socket = socket;
        }

        internal WebSocket Socket { get; }
        internal RateLimiter RateLimiter { get; } = new();
    }

    private sealed class RateLimiter
    {
        private readonly object _gate = new();
        private DateTimeOffset _windowStart = DateTimeOffset.UtcNow;
        private int _count;

        internal bool TryConsume(DateTimeOffset now, int maxPerSecond)
        {
            if (maxPerSecond <= 0)
            {
                return true;
            }

            lock (_gate)
            {
                if (now - _windowStart >= TimeSpan.FromSeconds(1))
                {
                    _windowStart = now;
                    _count = 0;
                }

                if (_count >= maxPerSecond)
                {
                    return false;
                }

                _count++;
                return true;
            }
        }
    }
}
