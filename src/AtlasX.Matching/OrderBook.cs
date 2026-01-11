// Invariants: Orders match by price-time priority; market orders never rest on book; trades use maker price.
namespace AtlasX.Matching;

/// <summary>
/// Maintains an in-memory order book with price-time priority matching.
/// </summary>
public sealed class OrderBook
{
    private readonly SortedDictionary<decimal, Queue<Order>> _bids;
    private readonly SortedDictionary<decimal, Queue<Order>> _asks;
    private readonly Dictionary<Guid, OrderIndexEntry> _orderIndex;
    private readonly string _symbol;

    /// <summary>
    /// Creates a new order book for the specified symbol.
    /// </summary>
    public OrderBook(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new ArgumentException("Symbol must be provided.", nameof(symbol));
        }

        _symbol = symbol;
        _bids = new SortedDictionary<decimal, Queue<Order>>(Comparer<decimal>.Create((x, y) => y.CompareTo(x)));
        _asks = new SortedDictionary<decimal, Queue<Order>>();
        _orderIndex = new Dictionary<Guid, OrderIndexEntry>();
    }

    /// <summary>
    /// Adds an order to the book and returns any resulting trades.
    /// </summary>
    public MatchResult AddOrder(Order order)
    {
        if (order is null)
        {
            throw new ArgumentNullException(nameof(order));
        }

        if (!string.Equals(order.Symbol, _symbol, StringComparison.Ordinal))
        {
            throw new ArgumentException("Order symbol does not match order book symbol.", nameof(order));
        }

        var trades = new List<Trade>();

        if (order.Side == OrderSide.Buy)
        {
            MatchBuyOrder(order, trades);
        }
        else
        {
            MatchSellOrder(order, trades);
        }

        if (order.RemainingQuantity > 0 && order.Type == OrderType.Limit)
        {
            AddRestingOrder(order);
            return new MatchResult(trades, order);
        }

        return new MatchResult(trades, null);
    }

    /// <summary>
    /// Cancels a resting order by its identifier if present.
    /// </summary>
    public void CancelOrder(Guid orderId)
    {
        if (!_orderIndex.TryGetValue(orderId, out var entry))
        {
            return;
        }

        var book = entry.Side == OrderSide.Buy ? _bids : _asks;
        if (!book.TryGetValue(entry.Price, out var queue))
        {
            _orderIndex.Remove(orderId);
            return;
        }

        if (queue.Count == 0)
        {
            book.Remove(entry.Price);
            _orderIndex.Remove(orderId);
            return;
        }

        var newQueue = new Queue<Order>(queue.Count);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Id != orderId)
            {
                newQueue.Enqueue(current);
            }
        }

        if (newQueue.Count > 0)
        {
            book[entry.Price] = newQueue;
        }
        else
        {
            book.Remove(entry.Price);
        }

        _orderIndex.Remove(orderId);
    }

    /// <summary>
    /// Returns a depth-limited snapshot of the order book.
    /// </summary>
    public OrderBookSnapshot Snapshot(int depth = 10)
    {
        if (depth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Depth must be greater than zero.");
        }

        var bids = BuildSnapshotLevels(_bids, depth);
        var asks = BuildSnapshotLevels(_asks, depth);
        return new OrderBookSnapshot(_symbol, bids, asks);
    }

    private void MatchBuyOrder(Order order, List<Trade> trades)
    {
        while (order.RemainingQuantity > 0 && TryGetBestLevel(_asks, out var askPrice, out var askQueue))
        {
            if (order.Type == OrderType.Limit && askPrice > order.Price)
            {
                break;
            }

            MatchAgainstLevel(order, askPrice, askQueue, trades, isBuyerTaker: true);

            if (askQueue.Count == 0)
            {
                _asks.Remove(askPrice);
            }
        }
    }

    private void MatchSellOrder(Order order, List<Trade> trades)
    {
        while (order.RemainingQuantity > 0 && TryGetBestLevel(_bids, out var bidPrice, out var bidQueue))
        {
            if (order.Type == OrderType.Limit && bidPrice < order.Price)
            {
                break;
            }

            MatchAgainstLevel(order, bidPrice, bidQueue, trades, isBuyerTaker: false);

            if (bidQueue.Count == 0)
            {
                _bids.Remove(bidPrice);
            }
        }
    }

    private void MatchAgainstLevel(
        Order taker,
        decimal makerPrice,
        Queue<Order> makerQueue,
        List<Trade> trades,
        bool isBuyerTaker)
    {
        while (taker.RemainingQuantity > 0 && makerQueue.Count > 0)
        {
            var maker = makerQueue.Peek();
            var fillQuantity = Math.Min(taker.RemainingQuantity, maker.RemainingQuantity);

            maker.DecreaseRemaining(fillQuantity);
            taker.DecreaseRemaining(fillQuantity);

            trades.Add(new Trade(
                Guid.NewGuid(),
                _symbol,
                makerPrice,
                fillQuantity,
                isBuyerTaker ? taker.Id : maker.Id,
                isBuyerTaker ? maker.Id : taker.Id,
                DateTime.UtcNow));

            if (maker.RemainingQuantity == 0)
            {
                makerQueue.Dequeue();
                _orderIndex.Remove(maker.Id);
            }
        }
    }

    private void AddRestingOrder(Order order)
    {
        var book = order.Side == OrderSide.Buy ? _bids : _asks;
        var price = order.Price ?? 0m;

        if (!book.TryGetValue(price, out var queue))
        {
            queue = new Queue<Order>();
            book[price] = queue;
        }

        queue.Enqueue(order);
        _orderIndex[order.Id] = new OrderIndexEntry(order.Side, price);
    }

    private static bool TryGetBestLevel(
        SortedDictionary<decimal, Queue<Order>> book,
        out decimal price,
        out Queue<Order> queue)
    {
        foreach (var level in book)
        {
            price = level.Key;
            queue = level.Value;
            return true;
        }

        price = 0m;
        queue = new Queue<Order>();
        return false;
    }

    private static IReadOnlyList<OrderBookLevel> BuildSnapshotLevels(
        SortedDictionary<decimal, Queue<Order>> book,
        int depth)
    {
        var levels = new List<OrderBookLevel>(depth);
        foreach (var level in book)
        {
            if (levels.Count >= depth)
            {
                break;
            }

            var totalQuantity = 0m;
            foreach (var order in level.Value)
            {
                totalQuantity += order.RemainingQuantity;
            }

            levels.Add(new OrderBookLevel(level.Key, totalQuantity, level.Value.Count));
        }

        return levels;
    }

    private readonly record struct OrderIndexEntry(OrderSide Side, decimal Price);
}

/// <summary>
/// Contains trades produced by matching and any resting order.
/// </summary>
public sealed record MatchResult(IReadOnlyList<Trade> Trades, Order? RestingOrder);

/// <summary>
/// Represents a point-in-time view of the book for a symbol.
/// </summary>
public sealed record OrderBookSnapshot(
    string Symbol,
    IReadOnlyList<OrderBookLevel> Bids,
    IReadOnlyList<OrderBookLevel> Asks);

/// <summary>
/// Represents aggregated size and order count at a price level.
/// </summary>
public sealed record OrderBookLevel(decimal Price, decimal Quantity, int OrderCount);
