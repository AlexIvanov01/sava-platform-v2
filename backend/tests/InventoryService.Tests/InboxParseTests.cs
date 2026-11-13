using System.Text.Json;
using InventoryService.Messaging;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

public class InboxParseTests
{
    private static readonly DateTime Now = TestData.Date(2026, 2027);

    [Theory]
    [InlineData("Order_Published")]
    [InlineData("Order_Deleted")]
    public void Parse_OrderEvent_IsReceivedUnderItsMessageId(string eventName)
    {
        var messageId = TestData.Faker.Random.Guid();
        var orderId = TestData.Faker.Random.Guid();
        var body = Inbox.OrderEvent(messageId, eventName, orderId, []);

        var message = InboxHandler.Parse(body, TestData.Faker.Random.Guid().ToString(), Now);

        message.Id.ShouldBe(messageId);
        message.Type.ShouldBe(eventName);
        message.AggregateId.ShouldBe(orderId);
        message.Payload.ShouldBe(body);
        message.Status.ShouldBe(InboxStatus.Received);
        message.LastError.ShouldBeNull();
        message.CreatedAt.ShouldBe(Now);
        message.ProcessedAt.ShouldBeNull();
    }

    [Fact]
    public void Parse_NoMessageIdInTheBody_UsesTheAmqpMessageId()
    {
        var amqpId = TestData.Faker.Random.Guid();
        var body = JsonSerializer.Serialize(
            new { OrderId = TestData.Faker.Random.Guid(), Event = "Order_Deleted" }
        );

        var message = InboxHandler.Parse(body, amqpId.ToString(), Now);

        message.Id.ShouldBe(amqpId);
        message.Status.ShouldBe(InboxStatus.Received);
    }

    public static TheoryData<string, string> Unusable() =>
        new()
        {
            { "not json", "The message is not a valid event: " },
            { "null", "The message is empty." },
            {
                "{\"Event\":\"Order_Published\",\"OrderId\":\"0b6f6d3e-5d0e-4b7a-9f1e-2c3a4b5c6d71\"}",
                "The message has no MessageId."
            },
            {
                "{\"MessageId\":\""
                    + "1c7a7e4f-6e1f-4c8b-8a2f-3d4b5c6d7e82"
                    + "\",\"Event\":\"Order_Shipped\",\"OrderId\":\""
                    + "2d8b8f50-7f20-4d9c-9b30-4e5c6d7e8f93"
                    + "\"}",
                "Unknown event 'Order_Shipped'."
            },
            {
                "{\"MessageId\":\"3e9c9061-8031-4ead-8c41-5f6d7e8f90a4\",\"Event\":\"Order_Published\"}",
                "The message has no OrderId."
            },
            {
                "{\"MessageId\":\""
                    + "4fad0172-9142-4fbe-9d52-607e8f90a1b5"
                    + "\",\"Event\":\"Order_Published\",\"OrderId\":\""
                    + Guid.Empty
                    + "\"}",
                "The message has no OrderId."
            },
        };

    [Theory]
    [MemberData(nameof(Unusable))]
    public void Parse_UnusableMessage_IsFailedWithoutAnOrder(string body, string reason)
    {
        var message = InboxHandler.Parse(body, null, Now);

        message.Id.ShouldNotBe(Guid.Empty);
        message.Status.ShouldBe(InboxStatus.Failed);
        message.LastError.ShouldNotBeNull().ShouldStartWith(reason);
        message.AggregateId.ShouldBeNull();
        message.Payload.ShouldBe(body);
        message.ProcessedAt.ShouldBe(Now);
    }
}
