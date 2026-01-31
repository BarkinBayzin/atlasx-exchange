using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace AtlasX.Infrastructure;

public sealed class RabbitMqConnectionManager : IRabbitMqConnectionManager, IDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionManager> _logger;
    private readonly ConcurrentBag<IModel> _channelPool = new();
    private readonly SemaphoreSlim _channelLimiter;
    private readonly object _connectionGate = new();
    private IConnection? _connection;
    private DateTimeOffset _nextReconnectAtUtc = DateTimeOffset.MinValue;
    private bool _disposed;

    public RabbitMqConnectionManager(RabbitMqOptions options, ILogger<RabbitMqConnectionManager> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var maxChannels = Math.Max(1, _options.MaxChannels);
        _channelLimiter = new SemaphoreSlim(maxChannels, maxChannels);
    }

    public async Task<IRabbitMqChannel> RentChannelAsync(CancellationToken cancellationToken)
    {
        await _channelLimiter.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var channel = GetOrCreateChannel();
            return new PooledRabbitMqChannel(channel, this);
        }
        catch
        {
            _channelLimiter.Release();
            throw;
        }
    }

    private IModel GetOrCreateChannel()
    {
        if (_channelPool.TryTake(out var channel))
        {
            if (channel.IsOpen)
            {
                return channel;
            }

            channel.Dispose();
        }

        var connection = EnsureConnection();
        return connection.CreateModel();
    }

    private IConnection EnsureConnection()
    {
        lock (_connectionGate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RabbitMqConnectionManager));
            }

            if (_connection is not null && _connection.IsOpen)
            {
                return _connection;
            }

            var now = DateTimeOffset.UtcNow;
            if (now < _nextReconnectAtUtc)
            {
                throw new InvalidOperationException("RabbitMQ connection backoff in effect.");
            }

            try
            {
                var host = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? _options.HostName;
                var factory = new ConnectionFactory { HostName = host };
                _connection = factory.CreateConnection();
                return _connection;
            }
            catch (Exception ex)
            {
                var backoffMs = Math.Max(1, _options.ReconnectBackoffMs);
                _nextReconnectAtUtc = now.AddMilliseconds(backoffMs);
                _logger.LogWarning(ex, "Failed to connect to RabbitMQ. Next attempt after backoff.");
                throw;
            }
        }
    }

    internal void ReturnChannel(IModel channel)
    {
        if (_disposed)
        {
            channel.Dispose();
            _channelLimiter.Release();
            return;
        }

        if (channel.IsOpen)
        {
            _channelPool.Add(channel);
        }
        else
        {
            channel.Dispose();
        }

        _channelLimiter.Release();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        while (_channelPool.TryTake(out var channel))
        {
            channel.Dispose();
        }

        _connection?.Dispose();
        _channelLimiter.Dispose();
    }

    private sealed class PooledRabbitMqChannel : IRabbitMqChannel
    {
        private readonly IModel _inner;
        private readonly RabbitMqConnectionManager _owner;
        private bool _disposed;

        public PooledRabbitMqChannel(IModel inner, RabbitMqConnectionManager owner)
        {
            _inner = inner;
            _owner = owner;
        }

        public void ConfirmSelect() => _inner.ConfirmSelect();

        public void ExchangeDeclare(string exchange, string type, bool durable, bool autoDelete)
            => _inner.ExchangeDeclare(exchange, type, durable: durable, autoDelete: autoDelete);

        public IRabbitMqBasicProperties CreateBasicProperties()
            => new RabbitMqBasicPropertiesAdapter(_inner.CreateBasicProperties());

        public void BasicPublish(string exchange, string routingKey, IRabbitMqBasicProperties properties, ReadOnlyMemory<byte> body)
        {
            var basicProperties = properties is RabbitMqBasicPropertiesAdapter adapter
                ? adapter.Inner
                : CreateBasicPropertiesFrom(properties);

            _inner.BasicPublish(exchange, routingKey, basicProperties, body.ToArray());
        }

        public bool WaitForConfirms(TimeSpan timeout) => _inner.WaitForConfirms(timeout);

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner.ReturnChannel(_inner);
        }

        private IBasicProperties CreateBasicPropertiesFrom(IRabbitMqBasicProperties properties)
        {
            var basic = _inner.CreateBasicProperties();
            basic.ContentType = properties.ContentType;
            basic.DeliveryMode = properties.DeliveryMode;
            return basic;
        }
    }

    private sealed class RabbitMqBasicPropertiesAdapter : IRabbitMqBasicProperties
    {
        public RabbitMqBasicPropertiesAdapter(IBasicProperties inner)
        {
            Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IBasicProperties Inner { get; }

        public string? ContentType
        {
            get => Inner.ContentType;
            set => Inner.ContentType = value;
        }

        public byte DeliveryMode
        {
            get => Inner.DeliveryMode;
            set => Inner.DeliveryMode = value;
        }
    }
}
