using System.Text.Json;
using InventoryService.DataAccess;
using InventoryService.Dtos;
using InventoryService.EventProcessing;
using Microsoft.Extensions.DependencyInjection;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

public sealed class EventProcessorTests : IDisposable
{
    private readonly IStockRepo _stock = Substitute.For<IStockRepo>();
    private readonly ServiceProvider _services;
    private readonly EventProcessor _processor;

    public EventProcessorTests()
    {
        _stock
            .SetOrderStockAsync(Guid.Empty, default!)
            .ReturnsForAnyArgs(Task.FromResult<IReadOnlyList<Guid>>([]));
        _services = new ServiceCollection().AddScoped(_ => _stock).BuildServiceProvider();
        _processor = new EventProcessor(_services.GetRequiredService<IServiceScopeFactory>());
    }

    public void Dispose() => _services.Dispose();

    private static string Message(
        string eventName,
        Guid? orderId,
        params OrderItemPublishedDto[] items
    ) => JsonSerializer.Serialize(new OrderPublishedDto(orderId, items, eventName));

    private static OrderItemPublishedDto Item(params (Guid BatchId, int Quantity)[] batches) =>
        new(
            TestData.Faker.Random.Guid(),
            [.. batches.Select(b => new OrderItemBatchPublishedDto(b.BatchId, b.Quantity))]
        );

    private static OrderItemPublishedDto AnyItem() =>
        Item((TestData.Faker.Random.Guid(), TestData.Faker.Random.Int(1, 10)));

    [Fact]
    public async Task OrderPublished_SetsTheOrdersStockToTheBatchesOfAllItems()
    {
        var orderId = TestData.Faker.Random.Guid();
        var a = TestData.Faker.Random.Guid();
        var b = TestData.Faker.Random.Guid();
        var c = TestData.Faker.Random.Guid();

        await _processor.ProcessEvent(
            Message("Order_Published", orderId, Item((a, 3), (b, 1)), Item((c, 7)))
        );

        await _stock
            .Received(1)
            .SetOrderStockAsync(
                orderId,
                Arg.Is<IReadOnlyDictionary<Guid, int>>(t =>
                    t.Count == 3 && t[a] == 3 && t[b] == 1 && t[c] == 7
                )
            );
    }

    // Order validates against this, but a hand-written message could still repeat a batch.
    [Fact]
    public async Task OrderPublished_SameBatchInDifferentItems_IsSummed()
    {
        var orderId = TestData.Faker.Random.Guid();
        var a = TestData.Faker.Random.Guid();
        var b = TestData.Faker.Random.Guid();

        await _processor.ProcessEvent(
            Message("Order_Published", orderId, Item((a, 3), (b, 1)), Item((a, 2)))
        );

        await _stock
            .Received(1)
            .SetOrderStockAsync(
                orderId,
                Arg.Is<IReadOnlyDictionary<Guid, int>>(t => t.Count == 2 && t[a] == 5 && t[b] == 1)
            );
    }

    [Fact]
    public async Task OrderDeleted_SetsTheOrdersStockToNothing()
    {
        var orderId = TestData.Faker.Random.Guid();
        _stock.OrderHasMovementsAsync(orderId).Returns(true);

        await _processor.ProcessEvent(Message("Order_Deleted", orderId, AnyItem()));

        await _stock
            .Received(1)
            .SetOrderStockAsync(orderId, Arg.Is<IReadOnlyDictionary<Guid, int>>(t => t.Count == 0));
    }

    [Fact]
    public async Task OrderDeleted_OrderUnknownToTheLedger_DoesNothing()
    {
        var orderId = TestData.Faker.Random.Guid();
        _stock.OrderHasMovementsAsync(orderId).Returns(false);

        await _processor.ProcessEvent(Message("Order_Deleted", orderId, AnyItem()));

        await _stock.DidNotReceiveWithAnyArgs().SetOrderStockAsync(Guid.Empty, default!);
    }

    [Fact]
    public async Task OrderPublished_WithoutItems_IsRejected()
    {
        await _processor.ProcessEvent(Message("Order_Published", TestData.Faker.Random.Guid()));

        _stock.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task OrderPublished_ItemsWithoutBatches_IsRejected()
    {
        await _processor.ProcessEvent(
            Message("Order_Published", TestData.Faker.Random.Guid(), Item(), Item())
        );

        _stock.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task OrderPublished_NullItems_IsRejected()
    {
        await _processor.ProcessEvent(
            JsonSerializer.Serialize(
                new OrderPublishedDto(TestData.Faker.Random.Guid(), null, "Order_Published")
            )
        );

        _stock.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Order_Published")]
    [InlineData("Order_Deleted")]
    public async Task MessageWithoutOrderId_IsSkipped(string eventName)
    {
        await _processor.ProcessEvent(Message(eventName, null, AnyItem()));
        await _processor.ProcessEvent(Message(eventName, Guid.Empty, AnyItem()));

        _stock.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("{\"Event\":\"Customer_Published\"}")]
    [InlineData("{\"Event\":\"\"}")]
    [InlineData("null")]
    public async Task ProcessEvent_UnknownEvent_IsIgnored(string message)
    {
        await _processor.ProcessEvent(message);

        _stock.ReceivedCalls().ShouldBeEmpty();
    }
}
