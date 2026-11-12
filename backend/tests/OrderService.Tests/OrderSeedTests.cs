using Microsoft.EntityFrameworkCore;
using NSubstitute.ExceptionExtensions;
using OrderService.DataAccess;
using OrderService.Mappings;
using OrderService.Models;
using OrderService.Ordering;
using OrderService.SyncDataServices.Grpc;
using Sava.Tests.Shared;

namespace OrderService.Tests;

// PrepDB.SeedOrdersAsync against substituted gRPC clients that answer from the Customer and
// Inventory seed files.
public class OrderSeedTests(MySqlFixture mySql) : OrderDbTestBase(mySql)
{
    private readonly IInventoryDataClient _inventory = Substitute.For<IInventoryDataClient>();
    private readonly ICustomerDataClient _customers = Substitute.For<ICustomerDataClient>();

    private void ServeSeedData()
    {
        _inventory.Serves(SeedData.Catalog());
        foreach (var (id, customer) in SeedData.Customers())
        {
            _customers.GetCustomerAsync(id).Returns(customer);
        }
    }

    private async Task SeedOrdersAsync()
    {
        await using var context = Db.CreateContext();
        await PrepDB.SeedOrdersAsync(context, _inventory, _customers, SeedData.OrdersFile);
    }

    private async Task<List<Order>> LoadOrdersAsync()
    {
        await using var context = Db.CreateContext();
        return await context
            .Orders.Include(o => o.Invoice)
            .Include(o => o.Items)
                .ThenInclude(l => l.Batches)
            .AsSplitQuery()
            .ToListAsync(Ct);
    }

    [Fact]
    public async Task SeedOrdersAsync_SeedsTheOrdersWithSnapshotsOnce()
    {
        ServeSeedData();
        var seeds = SeedData.Orders();
        var catalog = SeedData.Catalog();
        var customers = SeedData.Customers();

        await SeedOrdersAsync();
        await SeedOrdersAsync();

        var orders = await LoadOrdersAsync();
        orders.Select(o => o.Id).ShouldBe(seeds.Select(s => s.Id), ignoreOrder: true);
        foreach (var seed in seeds)
        {
            var order = orders.Single(o => o.Id == seed.Id);
            order.OrderDate.ShouldBe(seed.OrderDate);
            order.ShippedDate.ShouldBe(seed.ShippedDate);
            order.ShippingAddress.ShouldBe(seed.ShippingAddress);
            order.OrderStatus.ShouldBe(seed.OrderStatus);
            order.CustomerId.ShouldBe(seed.CustomerId);
            order
                .Customer.ShouldNotBeNull()
                .ToReadDto()
                .ShouldBe(customers[seed.CustomerId].ToReadDto());
            order
                .Items.SelectMany(l => l.Batches.Select(b => (l.ProductId, b.BatchId, b.Quantity)))
                .ShouldBe(
                    seed.Items.SelectMany(l =>
                        l.Batches!.Select(b =>
                            (l.ProductId!.Value, b.BatchId!.Value, b.Quantity!.Value)
                        )
                    ),
                    ignoreOrder: true
                );
            foreach (var item in order.Items)
            {
                var product = catalog.Products[item.ProductId];
                (item.SKU, item.Name, item.UnitPrice, item.VatRate, item.Cost).ShouldBe(
                    (product.SKU, product.Name, product.ListPrice, product.VatRate, product.Cost)
                );
                item.Batches.ShouldAllBe(b => b.Lot == catalog.Batches[b.BatchId].Lot);
            }

            if (seed.Invoice == null)
            {
                order.Invoice.ShouldBeNull();
            }
            else
            {
                var invoice = order.Invoice.ShouldNotBeNull();
                invoice.InvoiceDate.ShouldBe(seed.Invoice.InvoiceDate);
                invoice.InvoiceStatus.ShouldBe(seed.Invoice.InvoiceStatus);
                invoice.TotalAmount.ShouldBe(order.Items.Gross());
            }
        }
        orders.Count(o => o.Invoice != null).ShouldBe(seeds.Count(s => s.Invoice != null));
        // The second run found orders and didn't ask the other services again.
        await _inventory.ReceivedWithAnyArgs(1).GetOrderSnapshotAsync(default!, default!);
    }

    [Fact]
    public async Task SeedOrdersAsync_AsksInventoryForEveryProductAndBatchOnce()
    {
        ServeSeedData();
        var items = SeedData.Orders().SelectMany(s => s.Items).ToList();
        var productIds = items.Select(l => l.ProductId!.Value).Distinct().ToList();
        var batchIds = items
            .SelectMany(l => l.Batches!)
            .Select(b => b.BatchId!.Value)
            .Distinct()
            .ToList();

        await SeedOrdersAsync();

        await _inventory
            .Received(1)
            .GetOrderSnapshotAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Order().SequenceEqual(productIds.Order())
                ),
                Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.Order().SequenceEqual(batchIds.Order())
                )
            );
    }

    [Theory]
    [InlineData("InventoryService")]
    [InlineData("CustomerService")]
    public async Task SeedOrdersAsync_ServiceUnavailable_SeedsNothing(string service)
    {
        ServeSeedData();
        var unavailable = new UpstreamUnavailableException(
            service,
            new InvalidOperationException()
        );
        if (service == "InventoryService")
        {
            _inventory.GetOrderSnapshotAsync(default!, default!).ThrowsAsyncForAnyArgs(unavailable);
        }
        else
        {
            _customers.GetCustomerAsync(Arg.Any<Guid>()).ThrowsAsync(unavailable);
        }

        await SeedOrdersAsync();

        (await LoadOrdersAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task SeedOrdersAsync_CustomerMissing_SeedsNothing()
    {
        ServeSeedData();
        var missing = TestData.Faker.PickRandom(SeedData.Orders()).CustomerId;
        _customers.GetCustomerAsync(missing).Returns((CustomerSnapshot?)null);

        await SeedOrdersAsync();

        (await LoadOrdersAsync()).ShouldBeEmpty();
    }

    // Inventory doesn't know one of the batches (e.g. its seed data changed): no partial seed.
    [Fact]
    public async Task SeedOrdersAsync_InvalidItems_SeedsNothing()
    {
        ServeSeedData();
        var catalog = SeedData.Catalog();
        var missing = TestData
            .Faker.PickRandom(
                SeedData.Orders().SelectMany(s => s.Items).SelectMany(l => l.Batches!).ToList()
            )
            .BatchId!.Value;
        _inventory.Serves(
            OrderBuilder.Catalog(
                catalog.Products.Values,
                catalog.Batches.Values.Where(b => b.Id != missing)
            )
        );

        await SeedOrdersAsync();

        (await LoadOrdersAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task SeedOrdersAsync_OrdersPresent_DoesNothing()
    {
        ServeSeedData();
        var existing = await SeedOrderAsync(1);

        await SeedOrdersAsync();

        (await LoadOrdersAsync()).ShouldHaveSingleItem().Id.ShouldBe(existing.Id);
        _inventory.ReceivedCalls().ShouldBeEmpty();
        _customers.ReceivedCalls().ShouldBeEmpty();
    }
}

// The three services' seed files share ids; the order seed only works while they agree.
public class OrderSeedFileTests
{
    [Fact]
    public void OrderIds_AreUnique()
    {
        var ids = SeedData.Orders().Select(o => o.Id).ToList();

        ids.ShouldNotBeEmpty();
        ids.ShouldBeUnique();
    }

    [Fact]
    public void Customers_ExistInTheCustomerSeed()
    {
        var customers = SeedData.Customers();

        SeedData.Orders().ShouldAllBe(o => customers.ContainsKey(o.CustomerId));
    }

    [Fact]
    public void Items_ReferenceInventoryProductsAndTheirOwnBatches()
    {
        var catalog = SeedData.Catalog();

        foreach (var order in SeedData.Orders())
        {
            order.Items.ShouldNotBeEmpty($"Order {order.Id} has no items.");
            foreach (var item in order.Items)
            {
                var productId = item.ProductId.ShouldNotBeNull();
                catalog.Products.ShouldContainKey(productId, $"Order {order.Id}");
                catalog.Products[productId].ListPrice.ShouldNotBeNull($"Product {productId}");
                item.Batches.ShouldNotBeNull().ShouldNotBeEmpty($"Order {order.Id}");
                foreach (var batch in item.Batches)
                {
                    var batchId = batch.BatchId.ShouldNotBeNull();
                    catalog.Batches.ShouldContainKey(batchId, $"Order {order.Id}");
                    catalog.Batches[batchId].ProductId.ShouldBe(productId, $"Batch {batchId}");
                    batch.Quantity.ShouldNotBeNull().ShouldBeGreaterThan(0);
                }
            }
        }
    }

    // What the API would reject (OrderCreateDto.Validate) the seed shouldn't contain either.
    [Fact]
    public void Orders_HaveNoDuplicateProductsOrBatches()
    {
        foreach (var order in SeedData.Orders())
        {
            order.Items.Select(l => l.ProductId).ShouldBeUnique($"Order {order.Id}");
            order
                .Items.SelectMany(l => l.Batches!)
                .Select(b => b.BatchId)
                .ShouldBeUnique($"Order {order.Id}");
        }
    }

    [Fact]
    public void Orders_ApplyToTheSeedCatalogWithoutErrors()
    {
        var catalog = SeedData.Catalog();

        foreach (var seed in SeedData.Orders())
        {
            OrderItems.Apply(new Order(), seed.Items, catalog).ShouldBeEmpty($"Order {seed.Id}");
        }
    }
}
