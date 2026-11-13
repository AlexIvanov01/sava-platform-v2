using System.Text;
using CustomerService.AsyncDataServices;
using CustomerService.Messaging;
using NSubstitute.ExceptionExtensions;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Sava.Tests.Shared;

namespace CustomerService.Tests;

public class MessageBusSubscriberTests
{
    private readonly IChannel _channel = Substitute.For<IChannel>();
    private readonly IInboxHandler _inbox = Substitute.For<IInboxHandler>();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static BasicDeliverEventArgs Delivery(
        ulong tag,
        string message,
        string? messageId = null
    ) =>
        new(
            "consumer",
            tag,
            false,
            "exchange",
            "key",
            new BasicProperties { MessageId = messageId },
            Encoding.UTF8.GetBytes(message),
            Ct
        );

    private Task HandleAsync(BasicDeliverEventArgs delivery) =>
        MessageBusSubscriber.HandleDeliveryAsync(_channel, _inbox, delivery, TimeSpan.Zero, Ct);

    [Fact]
    public async Task HandledMessage_IsAcknowledged()
    {
        var message = TestData.Faker.Lorem.Sentence();
        var messageId = TestData.Faker.Random.Guid().ToString();
        var tag = (ulong)TestData.Faker.Random.Int(1, 1000);
        _inbox.HandleAsync(message, messageId, Ct).Returns(DeliveryOutcome.Ack);

        await HandleAsync(Delivery(tag, message, messageId));

        await _channel.Received(1).BasicAckAsync(tag, false, Ct);
        await _channel.DidNotReceiveWithAnyArgs().BasicNackAsync(default, default, default, Ct);
    }

    [Fact]
    public async Task MessageToRetry_IsRequeued()
    {
        var tag = (ulong)TestData.Faker.Random.Int(1, 1000);
        _inbox
            .HandleAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(DeliveryOutcome.Retry);

        await HandleAsync(Delivery(tag, "{}"));

        await _channel.Received(1).BasicNackAsync(tag, false, true, Ct);
        await _channel.DidNotReceiveWithAnyArgs().BasicAckAsync(default, default, Ct);
    }

    // Not recorded (e.g. the database is down) means not acknowledged: the message goes back to
    // the queue, every time, so it is never lost.
    [Fact]
    public async Task MessageThatCantBeRecorded_IsRequeued()
    {
        var tag = (ulong)TestData.Faker.Random.Int(1, 1000);
        _inbox
            .HandleAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException());

        await HandleAsync(Delivery(tag, "{}"));

        await _channel.Received(1).BasicNackAsync(tag, false, true, Ct);
        await _channel.DidNotReceiveWithAnyArgs().BasicAckAsync(default, default, Ct);
    }
}
