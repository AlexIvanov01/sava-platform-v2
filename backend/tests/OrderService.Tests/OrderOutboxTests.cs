using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderService.Dtos;
using OrderService.Messaging;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

// OrderContext turns the events raised on an order into an outbox message when it saves.
public class OrderOutboxTests(MySqlFixture mySql) : OrderDbTestBase(mySql)
{
    private static List<(Guid ProductId, Guid BatchId, int Quantity)> Batches(
        IEnumerable<OrderItemPublishedDto> items
    ) => [.. items.SelectMany(i => i.Batches.Select(b => (i.ProductId, b.BatchId, b.Quantity)))];

    [Fact]
    public async Task SavingAnOrderWithRaisedEvents_WritesOneMessageForTheNewestFromItsSavedState()
    {
        var order = OrderBuilder.Order(OrderBuilder.Item(batchCount: 2));
        order.Raise(new OrderDeleted());
        order.Raise(new OrderPublished());
        // A change after raising is part of the message: it describes the order as saved.
        order.Items.Add(OrderBuilder.Item());

        await using (var context = Db.CreateContext())
        {
            context.Orders.Add(order);
            await context.SaveChangesAsync(Ct);
        }

        order.Events.ShouldBeEmpty();
        await using var assert = Db.CreateContext();
        var message = await assert.OutboxMessages.SingleAsync(Ct);
        message.Type.ShouldBe("Order_Published");
        message.AggregateId.ShouldBe(order.Id);
        message.Status.ShouldBe(OutboxStatus.Pending);
        message.Attempts.ShouldBe(0);
        message.Sequence.ShouldBeGreaterThan(0);
        var published = JsonSerializer.Deserialize<OrderPublishedEvent>(message.Payload!)!;
        published.MessageId.ShouldBe(message.Id);
        published.Event.ShouldBe("Order_Published");
        published.OrderId.ShouldBe(order.Id);
        published.CustomerId.ShouldBe(order.CustomerId!.Value);
        Batches(published.Items)
            .ShouldBe(
                order.Items.SelectMany(i =>
                    i.Batches.Select(b => (i.ProductId, b.BatchId, b.Quantity))
                ),
                ignoreOrder: true
            );
    }

    [Fact]
    public async Task SavingWithoutRaisedEvents_WritesNoMessage()
    {
        await SeedOrderAsync();

        await using var assert = Db.CreateContext();
        (await assert.OutboxMessages.AnyAsync(Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task EachSave_GetsTheNextSequence()
    {
        var first = OrderBuilder.Order(OrderBuilder.Item());
        var second = OrderBuilder.Order(OrderBuilder.Item());
        first.Raise(new OrderPublished());
        second.Raise(new OrderPublished());
        await SeedAsync(first);
        await SeedAsync(second);

        await using var assert = Db.CreateContext();
        var messages = await assert.OutboxMessages.OrderBy(m => m.Sequence).ToListAsync(Ct);
        messages.Select(m => m.AggregateId).ShouldBe([first.Id, second.Id]);
        messages[1].Sequence.ShouldBeGreaterThan(messages[0].Sequence);
    }
}

public class RetryTests
{
    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 40)]
    [InlineData(5, 160)]
    [InlineData(6, 300)]
    [InlineData(10, 300)]
    public void NextAttemptAt_DoublesTheDelayUpToTheMaximum(int attempts, int seconds)
    {
        var now = TestData.Date(2026, 2027);
        var options = new MessagingOptions
        {
            RetryBaseDelaySeconds = 10,
            RetryMaxDelaySeconds = 300,
        };

        Retry.NextAttemptAt(attempts, now, options).ShouldBe(now.AddSeconds(seconds));
    }
}
