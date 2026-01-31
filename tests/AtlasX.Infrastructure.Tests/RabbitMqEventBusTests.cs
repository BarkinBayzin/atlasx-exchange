using AtlasX.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AtlasX.Infrastructure.Tests;

public class RabbitMqEventBusTests
{
    [Fact]
    public async Task Publish_waits_for_confirm_on_success()
    {
        var manager = new FakeConnectionManager();
        var options = Options.Create(new RabbitMqOptions { ConfirmTimeoutMs = 100 });
        var bus = new RabbitMqEventBus(manager, options, NullLogger<RabbitMqEventBus>.Instance);
        var evt = new OrderAccepted(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "BTC-USD", "BUY", "LIMIT", 1m, 100m);

        await bus.PublishAsync(evt, CancellationToken.None);

        Assert.Equal(1, manager.PublishCount);
        Assert.True(manager.LastChannel!.ExchangeDeclared);
        Assert.True(manager.LastChannel.ConfirmSelectCalled);
        Assert.True(manager.LastChannel.WaitForConfirmsCalled);
    }

    [Fact]
    public async Task Publish_timeout_throws()
    {
        var manager = new FakeConnectionManager { ConfirmResult = false };
        var options = Options.Create(new RabbitMqOptions { ConfirmTimeoutMs = 10 });
        var bus = new RabbitMqEventBus(manager, options, NullLogger<RabbitMqEventBus>.Instance);
        var evt = new OrderMatched(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "BTC-USD", 1);

        await Assert.ThrowsAsync<TimeoutException>(() => bus.PublishAsync(evt, CancellationToken.None));
    }

    [Fact]
    public async Task Connection_manager_reuses_connection()
    {
        var manager = new FakeConnectionManager();
        var options = Options.Create(new RabbitMqOptions { ConfirmTimeoutMs = 100 });
        var bus = new RabbitMqEventBus(manager, options, NullLogger<RabbitMqEventBus>.Instance);
        var evt1 = new OrderAccepted(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "BTC-USD", "BUY", "LIMIT", 1m, 100m);
        var evt2 = new OrderMatched(Guid.NewGuid(), DateTime.UtcNow, Guid.NewGuid(), "BTC-USD", 1);

        await bus.PublishAsync(evt1, CancellationToken.None);
        await bus.PublishAsync(evt2, CancellationToken.None);

        Assert.Equal(1, manager.ConnectionCreateCount);
        Assert.Equal(2, manager.PublishCount);
    }

    private sealed class FakeConnectionManager : IRabbitMqConnectionManager
    {
        private int _connectionCreated;

        public int ConnectionCreateCount => _connectionCreated;
        public int PublishCount { get; private set; }
        public bool ConfirmResult { get; set; } = true;
        internal FakeChannel? LastChannel { get; private set; }

        public Task<IRabbitMqChannel> RentChannelAsync(CancellationToken cancellationToken)
        {
            if (_connectionCreated == 0)
            {
                _connectionCreated = 1;
            }

            var channel = new FakeChannel(this, ConfirmResult);
            LastChannel = channel;
            return Task.FromResult<IRabbitMqChannel>(channel);
        }

        internal sealed class FakeChannel : IRabbitMqChannel
        {
            private readonly FakeConnectionManager _owner;
            private readonly bool _confirmResult;

            public FakeChannel(FakeConnectionManager owner, bool confirmResult)
            {
                _owner = owner;
                _confirmResult = confirmResult;
            }

            public bool ExchangeDeclared { get; private set; }
            public bool ConfirmSelectCalled { get; private set; }
            public bool WaitForConfirmsCalled { get; private set; }

            public void ExchangeDeclare(string exchange, string type, bool durable, bool autoDelete)
            {
                ExchangeDeclared = true;
            }

            public void ConfirmSelect()
            {
                ConfirmSelectCalled = true;
            }

            public IRabbitMqBasicProperties CreateBasicProperties()
                => new FakeProperties();

            public void BasicPublish(string exchange, string routingKey, IRabbitMqBasicProperties properties, ReadOnlyMemory<byte> body)
            {
                _owner.PublishCount++;
            }

            public bool WaitForConfirms(TimeSpan timeout)
            {
                WaitForConfirmsCalled = true;
                return _confirmResult;
            }

            public void Dispose()
            {
            }

            private sealed class FakeProperties : IRabbitMqBasicProperties
            {
                public string? ContentType { get; set; }
                public byte DeliveryMode { get; set; }
            }
        }
    }
}
