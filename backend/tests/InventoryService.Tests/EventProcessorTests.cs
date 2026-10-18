using System.Text.Json;
using InventoryService.DataAccess;
using InventoryService.Dtos;
using InventoryService.EventProcessing;
using Microsoft.Extensions.DependencyInjection;

namespace InventoryService.Tests;

public sealed class EventProcessorTests : IDisposable
{
    private readonly IProductRepo _repo = Substitute.For<IProductRepo>();
    private readonly ServiceProvider _services;
    private readonly EventProcessor _processor;

    public EventProcessorTests()
    {
        _services = new ServiceCollection().AddScoped(_ => _repo).BuildServiceProvider();
        _processor = new EventProcessor(_services.GetRequiredService<IServiceScopeFactory>());
    }

    public void Dispose() => _services.Dispose();

    private static string Message(string eventName, params OrderItemPublishedDto[] items) =>
        JsonSerializer.Serialize(new OrderPublishedDto(items, eventName));

    [Fact]
    public async Task ProcessEvent_OrderPublished_DecrementsBatches()
    {
        var item = new OrderItemPublishedDto(Guid.NewGuid(), 3);

        await _processor.ProcessEvent(Message("Order_Published", item));

        await _repo
            .Received(1)
            .DecrementBatchesAsync(
                Arg.Is<IEnumerable<OrderItemPublishedDto>>(i => i.Single() == item)
            );
        await _repo.DidNotReceiveWithAnyArgs().IncrementBatchesAsync(default!);
    }

    [Fact]
    public async Task ProcessEvent_OrderDeleted_IncrementsBatches()
    {
        var item = new OrderItemPublishedDto(Guid.NewGuid(), 3);

        await _processor.ProcessEvent(Message("Order_Deleted", item));

        await _repo
            .Received(1)
            .IncrementBatchesAsync(
                Arg.Is<IEnumerable<OrderItemPublishedDto>>(i => i.Single() == item)
            );
        await _repo.DidNotReceiveWithAnyArgs().DecrementBatchesAsync(default!);
    }

    [Theory]
    [InlineData("Order_Published")]
    [InlineData("Order_Deleted")]
    public async Task ProcessEvent_NoItems_DoesNothing(string eventName)
    {
        await _processor.ProcessEvent(Message(eventName));

        _repo.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("{\"Event\":\"Customer_Published\"}")]
    [InlineData("{\"Event\":\"\"}")]
    [InlineData("null")]
    public async Task ProcessEvent_UnknownEvent_IsIgnored(string message)
    {
        await _processor.ProcessEvent(message);

        _repo.ReceivedCalls().ShouldBeEmpty();
    }
}
