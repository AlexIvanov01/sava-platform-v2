using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute.ExceptionExtensions;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.EventProcessing;
using OrderService.Models;

namespace OrderService.Tests;

public class EventProcessorTests
{
    private readonly IItemRepo _items = Substitute.For<IItemRepo>();
    private readonly ICustomerRepo _customers = Substitute.For<ICustomerRepo>();
    private readonly EventProcessor _processor;

    public EventProcessorTests()
    {
        var provider = new ServiceCollection()
            .AddScoped(_ => _items)
            .AddScoped(_ => _customers)
            .BuildServiceProvider();
        _processor = new EventProcessor(provider.GetRequiredService<IServiceScopeFactory>());
    }

    private static string ItemEvent(
        string evt,
        Guid? batchId,
        Guid? productId,
        string? name = "Widget"
    ) =>
        JsonSerializer.Serialize(
            new ItemPublishedDto
            {
                Event = evt,
                ExternalBatchId = batchId,
                ExternalProductId = productId,
                Name = name,
                Price = 1.5m,
            }
        );

    private static string CustomerEvent(string evt, Guid id) =>
        JsonSerializer.Serialize(
            new
            {
                Event = evt,
                ExternalId = id,
                Name = "Ivan",
                City = "Sofia",
            }
        );

    [Fact]
    public async Task ItemPublished_NewItem_ChecksBatchIdAndAddsItem()
    {
        var batchId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        await _processor.ProcessEvent(ItemEvent("Item_Published", batchId, productId));

        await _items.Received(1).ExternalItemExistsAsync(batchId);
        await _items
            .Received(1)
            .AddItemAsync(
                Arg.Is<Item>(i => i.ExternalBatchId == batchId && i.ExternalProductId == productId)
            );
    }

    // Several batches share one product, so the existence check must use the batch id.
    [Fact]
    public async Task ItemPublished_SecondBatchOfSameProduct_IsAdded()
    {
        var productId = Guid.NewGuid();
        var existingBatch = Guid.NewGuid();
        _items.ExternalItemExistsAsync(existingBatch).Returns(true);
        _items.ExternalItemExistsAsync(productId).Returns(true);

        await _processor.ProcessEvent(ItemEvent("Item_Published", Guid.NewGuid(), productId));

        await _items.Received(1).AddItemAsync(Arg.Any<Item>());
    }

    [Fact]
    public async Task ItemPublished_ExistingBatch_IsSkipped()
    {
        var batchId = Guid.NewGuid();
        _items.ExternalItemExistsAsync(batchId).Returns(true);

        await _processor.ProcessEvent(ItemEvent("Item_Published", batchId, Guid.NewGuid()));

        await _items.DidNotReceiveWithAnyArgs().AddItemAsync(default!);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ItemPublished_MissingIds_IsIgnored(bool hasBatch, bool hasProduct)
    {
        await _processor.ProcessEvent(
            ItemEvent(
                "Item_Published",
                hasBatch ? Guid.NewGuid() : Guid.Empty,
                hasProduct ? Guid.NewGuid() : null
            )
        );

        await _items.DidNotReceiveWithAnyArgs().ExternalItemExistsAsync(default);
        await _items.DidNotReceiveWithAnyArgs().AddItemAsync(default!);
    }

    [Fact]
    public async Task ItemPublished_RepoThrows_IsSwallowed()
    {
        _items
            .ExternalItemExistsAsync(Arg.Any<Guid?>())
            .ThrowsAsync(new InvalidOperationException());

        await Should.NotThrowAsync(() =>
            _processor.ProcessEvent(ItemEvent("Item_Published", Guid.NewGuid(), Guid.NewGuid()))
        );
    }

    [Fact]
    public async Task ItemUpdated_UpdatesItem_OrIgnoresMissingBatchId()
    {
        var batchId = Guid.NewGuid();

        await _processor.ProcessEvent(ItemEvent("Item_Updated", batchId, null, "New name"));
        await _processor.ProcessEvent(ItemEvent("Item_Updated", null, null));

        await _items.Received(1).UpdateItemAsync(Arg.Any<Item>());
        await _items
            .Received(1)
            .UpdateItemAsync(
                Arg.Is<Item>(i => i.ExternalBatchId == batchId && i.Name == "New name")
            );
    }

    [Fact]
    public async Task ItemDeleted_DeletesByBatchId_OrIgnoresEmptyId()
    {
        var batchId = Guid.NewGuid();

        await _processor.ProcessEvent(ItemEvent("Item_Deleted", batchId, null));
        await _processor.ProcessEvent(ItemEvent("Item_Deleted", Guid.Empty, null));

        await _items.Received(1).DeleteItemAsync(Arg.Any<Guid?>());
        await _items.Received(1).DeleteItemAsync(batchId);
    }

    [Fact]
    public async Task ProductNameUpdated_RenamesAllBatches_OrIgnoresMissingName()
    {
        var productId = Guid.NewGuid();

        await _processor.ProcessEvent(ItemEvent("ProductName_Updated", null, productId, "Renamed"));
        await _processor.ProcessEvent(ItemEvent("ProductName_Updated", null, productId, null));

        await _items.Received(1).UpdateProductNameAsync(Arg.Any<Guid?>(), Arg.Any<string>());
        await _items.Received(1).UpdateProductNameAsync(productId, "Renamed");
    }

    [Fact]
    public async Task ProductDeleted_DeletesBatches_OrIgnoresEmptyId()
    {
        var productId = Guid.NewGuid();

        await _processor.ProcessEvent(ItemEvent("Product_Deleted", null, productId));
        await _processor.ProcessEvent(ItemEvent("Product_Deleted", null, Guid.Empty));

        await _items.Received(1).DeleteItemsByProductIdAsync(Arg.Any<Guid?>());
        await _items.Received(1).DeleteItemsByProductIdAsync(productId);
    }

    [Fact]
    public async Task CustomerPublished_NewCustomer_IsAdded()
    {
        var id = Guid.NewGuid();

        await _processor.ProcessEvent(CustomerEvent("Customer_Published", id));

        await _customers
            .Received(1)
            .AddCustomerAsync(
                Arg.Is<Customer>(c => c.ExternalId == id && c.Name == "Ivan" && c.City == "Sofia")
            );
    }

    [Fact]
    public async Task CustomerPublished_ExistingOrEmptyId_IsSkipped()
    {
        var id = Guid.NewGuid();
        _customers.ExternalCustomerExistsAsync(id).Returns(true);

        await _processor.ProcessEvent(CustomerEvent("Customer_Published", id));
        await _processor.ProcessEvent(CustomerEvent("Customer_Published", Guid.Empty));

        await _customers.DidNotReceiveWithAnyArgs().AddCustomerAsync(default!);
    }

    [Fact]
    public async Task CustomerUpdated_UpdatesCustomer_OrIgnoresEmptyId()
    {
        var id = Guid.NewGuid();

        await _processor.ProcessEvent(CustomerEvent("Customer_Updated", id));
        await _processor.ProcessEvent(CustomerEvent("Customer_Updated", Guid.Empty));

        await _customers.Received(1).UpdateCustomerAsync(Arg.Any<Customer>());
        await _customers.Received(1).UpdateCustomerAsync(Arg.Is<Customer>(c => c.ExternalId == id));
    }

    [Fact]
    public async Task CustomerDeleted_DeletesCustomer_OrIgnoresEmptyId()
    {
        var id = Guid.NewGuid();

        await _processor.ProcessEvent(CustomerEvent("Customer_Deleted", id));
        await _processor.ProcessEvent(CustomerEvent("Customer_Deleted", Guid.Empty));

        await _customers.Received(1).DeleteCustomerAsync(Arg.Any<Guid>());
        await _customers.Received(1).DeleteCustomerAsync(id);
    }

    [Theory]
    [InlineData("""{"Event":"Something_Else"}""")]
    [InlineData("""{}""")]
    [InlineData("null")]
    public async Task UnknownEvent_TouchesNoRepository(string message)
    {
        await _processor.ProcessEvent(message);

        _items.ReceivedCalls().ShouldBeEmpty();
        _customers.ReceivedCalls().ShouldBeEmpty();
    }
}
