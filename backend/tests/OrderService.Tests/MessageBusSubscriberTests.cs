using System.Text;
using NSubstitute.ExceptionExtensions;
using OrderService.AsyncDataServices;
using OrderService.EventProcessing;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class MessageBusSubscriberTests
{
    private readonly IChannel _channel = Substitute.For<IChannel>();
    private readonly IEventProcessor _processor = Substitute.For<IEventProcessor>();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BasicDeliverEventArgs Delivery(ulong tag, bool redelivered, string message) =>
        new(
            "consumer",
            tag,
            redelivered,
            "exchange",
            "key",
            new BasicProperties(),
            Encoding.UTF8.GetBytes(message),
            Ct
        );

    [Fact]
    public async Task ProcessedMessage_IsAcknowledged()
    {
        var message = TestData.Faker.Lorem.Sentence();
        var tag = (ulong)TestData.Faker.Random.Int(1, 1000);

        await MessageBusSubscriber.HandleDeliveryAsync(
            _channel,
            _processor,
            Delivery(tag, false, message),
            TimeSpan.Zero,
            Ct
        );

        await _processor.Received(1).ProcessEvent(message);
        await _channel.Received(1).BasicAckAsync(tag, false, Ct);
        await _channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, Ct);
    }

    [Fact]
    public async Task FirstFailure_IsRequeuedForOneRetry()
    {
        _processor.ProcessEvent(Arg.Any<string>()).ThrowsAsync(new InvalidOperationException());
        var tag = (ulong)TestData.Faker.Random.Int(1, 1000);

        await MessageBusSubscriber.HandleDeliveryAsync(
            _channel,
            _processor,
            Delivery(tag, false, "{}"),
            TimeSpan.Zero,
            Ct
        );

        await _channel.Received(1).BasicNackAsync(tag, false, true, Ct);
        await _channel.DidNotReceiveWithAnyArgs().BasicAckAsync(default, default, Ct);
    }

    [Fact]
    public async Task FailedRedelivery_IsDroppedSoItCantBlockTheQueue()
    {
        _processor.ProcessEvent(Arg.Any<string>()).ThrowsAsync(new InvalidOperationException());
        var tag = (ulong)TestData.Faker.Random.Int(1, 1000);

        await MessageBusSubscriber.HandleDeliveryAsync(
            _channel,
            _processor,
            Delivery(tag, true, "{}"),
            TimeSpan.Zero,
            Ct
        );

        await _channel.Received(1).BasicNackAsync(tag, false, false, Ct);
        await _channel.DidNotReceiveWithAnyArgs().BasicAckAsync(default, default, Ct);
    }
}
