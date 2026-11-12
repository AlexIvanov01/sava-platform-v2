using Microsoft.EntityFrameworkCore;
using OrderService.DataAccess;
using OrderService.Mappings;
using OrderService.Models;
using OrderService.Ordering;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class OrderRepoTests(MySqlFixture mySql) : OrderDbTestBase(mySql)
{
    private static List<(Guid ProductId, Guid BatchId, string? Lot, int Quantity)> Batches(
        Order order
    ) =>
        [
            .. order
                .Items.SelectMany(l =>
                    l.Batches.Select(b => (l.ProductId, b.BatchId, b.Lot, b.Quantity))
                )
                .OrderBy(b => b.BatchId),
        ];

    private static (
        Guid ProductId,
        string? SKU,
        string? Name,
        string? UnitOfMeasure,
        decimal? ListPrice,
        decimal? UnitPrice,
        decimal? VatRate,
        decimal? Cost,
        int Quantity
    ) Snapshot(OrderItem l) =>
        (
            l.ProductId,
            l.SKU,
            l.Name,
            l.UnitOfMeasure,
            l.ListPrice,
            l.UnitPrice,
            l.VatRate,
            l.Cost,
            l.Quantity
        );

    [Fact]
    public async Task Migrations_MatchTheModel()
    {
        await using var context = Db.CreateContext();

        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task CreateOrderAsync_PersistsItemsBatchesAndCustomerSnapshot()
    {
        var order = OrderBuilder.Order(OrderBuilder.Item(batchCount: 2), OrderBuilder.Item());
        var presetId = order.Id;

        await using (var context = Db.CreateContext())
        {
            await new OrderRepo(context).CreateOrderAsync(order);
        }

        order.Id.ShouldNotBe(presetId);
        await using var assert = Db.CreateContext();
        var saved = (await new OrderRepo(assert).GetOrderByIdAsync(order.Id)).ShouldNotBeNull();
        saved.CustomerId.ShouldBe(order.CustomerId);
        saved.OrderDate.ShouldBe(order.OrderDate);
        saved.ShippingAddress.ShouldBe(order.ShippingAddress);
        saved.OrderStatus.ShouldBe(order.OrderStatus);
        saved.Customer.ShouldNotBeNull().ToReadDto().ShouldBe(order.Customer!.ToReadDto());
        saved.CreatedAt.ShouldNotBeNull();
        // Everything a item snapshots.
        saved.Items.Select(Snapshot).ShouldBe(order.Items.Select(Snapshot), ignoreOrder: true);
        foreach (var item in order.Items)
        {
            saved
                .Items.Single(l => l.ProductId == item.ProductId)
                .ToReadDto()
                .Batches.ShouldBe(item.ToReadDto().Batches);
        }
        (await assert.OrderItems.CountAsync(Ct)).ShouldBe(2);
        (await assert.OrderItemBatches.CountAsync(Ct)).ShouldBe(3);
    }

    // Saving a customer snapshot whose fields are all empty still needs no customer.
    [Fact]
    public async Task CreateOrderAsync_EmptyCustomerSnapshot_RoundTrips()
    {
        var order = OrderBuilder.Order(OrderBuilder.Item());
        order.Customer = new CustomerSnapshot();

        await using (var context = Db.CreateContext())
        {
            await new OrderRepo(context).CreateOrderAsync(order);
        }

        await using var assert = Db.CreateContext();
        (await new OrderRepo(assert).GetOrderByIdAsync(order.Id))
            .ShouldNotBeNull()
            .Customer.ShouldNotBeNull()
            .Name.ShouldBeNull();
    }

    [Fact]
    public async Task SameProductTwiceOnAnOrder_IsRejectedByTheDatabase()
    {
        var item = OrderBuilder.Item();
        var duplicate = OrderBuilder.Item();
        duplicate.ProductId = item.ProductId;

        await using var context = Db.CreateContext();
        await Should.ThrowAsync<DbUpdateException>(
            new OrderRepo(context).CreateOrderAsync(OrderBuilder.Order(item, duplicate))
        );
    }

    [Fact]
    public async Task GetAllOrdersAsync_PagesByIdCursor()
    {
        for (var i = 0; i < 3; i++)
        {
            await SeedOrderAsync(1);
        }

        await using var context = Db.CreateContext();
        var repo = new OrderRepo(context);
        var first = (await repo.GetAllOrdersAsync(null, 2)).ToList();
        var second = (await repo.GetAllOrdersAsync(first[^1].Id, 2)).ToList();

        first.Count.ShouldBe(2);
        first[0].Customer.ShouldNotBeNull();
        first[0].Items.ShouldHaveSingleItem().Batches.ShouldHaveSingleItem();
        first.Select(o => o.Id).ShouldNotContain(second.ShouldHaveSingleItem().Id);
        (await repo.GetOrderCountAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task GetAllOrdersByCustomerIdAsync_FiltersAndPages()
    {
        var customerId = TestData.Faker.Random.Guid();
        var orders = new[]
        {
            OrderBuilder.OrderFor(customerId, OrderBuilder.Item()),
            OrderBuilder.OrderFor(customerId, OrderBuilder.Item()),
        };
        await SeedAsync(orders[0], orders[1]);
        await SeedOrderAsync();

        await using var context = Db.CreateContext();
        var repo = new OrderRepo(context);
        var first = (
            await repo.GetAllOrdersByCustomerIdAsync(customerId, null, 1)
        ).ShouldHaveSingleItem();
        var second = (
            await repo.GetAllOrdersByCustomerIdAsync(customerId, first.Id, 10)
        ).ShouldHaveSingleItem();

        new[] { first.Id, second.Id }.ShouldBe(orders.Select(o => o.Id), ignoreOrder: true);
        second.Items.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GetOrderForUpdateAsync_Unknown_ReturnsNull()
    {
        await SeedOrderAsync(1);
        await using var context = Db.CreateContext();

        (
            await new OrderRepo(context).GetOrderForUpdateAsync(TestData.Faker.Random.Guid())
        ).ShouldBeNull();
    }

    // The update path of the controller: load tracked, apply the requested items, save.
    [Fact]
    public async Task GetOrderForUpdateAsync_ChangedItemsAndBatches_ArePersisted()
    {
        var keep = OrderBuilder.Item(batchCount: 2);
        var drop = OrderBuilder.Item(batchCount: 2);
        var order = OrderBuilder.Order(keep, drop);
        await SeedAsync(order);
        var product = OrderBuilder.Product();
        var newBatch = OrderBuilder.Batch(product.Id);
        var keptBatch = OrderBuilder.Batch(keep.ProductId);
        var price = TestData.Faker.Finance.Amount(1, 100);
        var newCustomer = OrderBuilder.Customer();
        var (keptQuantity, addedQuantity, newQuantity) = (
            TestData.Faker.Random.Int(1, 50),
            TestData.Faker.Random.Int(1, 50),
            TestData.Faker.Random.Int(1, 50)
        );

        await using (var context = Db.CreateContext())
        {
            var repo = new OrderRepo(context);
            var tracked = (await repo.GetOrderForUpdateAsync(order.Id)).ShouldNotBeNull();
            tracked.Items.Count.ShouldBe(2);
            tracked.Items.ShouldAllBe(l => l.Batches.Count == 2);
            OrderItems
                .Apply(
                    tracked,
                    [
                        OrderBuilder.ItemDto(
                            keep.ProductId,
                            (keep.Batches[0].BatchId, keptQuantity),
                            (keptBatch.Id, addedQuantity)
                        ) with
                        {
                            UnitPrice = price,
                        },
                        OrderBuilder.ItemDto(product.Id, (newBatch.Id, newQuantity)),
                    ],
                    OrderBuilder.Catalog([product], [newBatch, keptBatch])
                )
                .ShouldBeEmpty();
            tracked.Customer = newCustomer;
            tracked.UpdatedAt = DateTime.UtcNow;
            await repo.SaveChangesAsync();
        }

        await using var assert = Db.CreateContext();
        var saved = (await new OrderRepo(assert).GetOrderByIdAsync(order.Id)).ShouldNotBeNull();
        saved.Customer.ShouldNotBeNull().ToReadDto().ShouldBe(newCustomer.ToReadDto());
        saved.UpdatedAt.ShouldNotBeNull();
        saved.Items.Single(l => l.ProductId == keep.ProductId).UnitPrice.ShouldBe(price);
        saved.Items.Single(l => l.ProductId == product.Id).Name.ShouldBe(product.Name);
        Batches(saved)
            .ShouldBe(
                new[]
                {
                    (keep.ProductId, keep.Batches[0].BatchId, keep.Batches[0].Lot, keptQuantity),
                    (keep.ProductId, keptBatch.Id, keptBatch.Lot, addedQuantity),
                    (product.Id, newBatch.Id, newBatch.Lot, newQuantity),
                }.OrderBy(b => b.Item2)
            );
        // The dropped item and batch rows are deleted, not orphaned.
        (await assert.OrderItems.CountAsync(Ct)).ShouldBe(2);
        (await assert.OrderItemBatches.CountAsync(Ct)).ShouldBe(3);
    }

    [Fact]
    public async Task DeleteOrderAsync_RemovesOrderWithItsItemsAndBatches()
    {
        var order = OrderBuilder.Order(OrderBuilder.Item(batchCount: 2), OrderBuilder.Item());
        var other = await SeedOrderAsync(1);
        await SeedAsync(order);

        await using (var context = Db.CreateContext())
        {
            var repo = new OrderRepo(context);
            var (status, deleted) = await repo.DeleteOrderAsync(order.Id);
            status.ShouldBe(DeleteOrderStatus.Deleted);
            // Returned with its items, for the Order_Deleted event.
            Batches(deleted.ShouldNotBeNull()).ShouldBe(Batches(order));
            (await repo.DeleteOrderAsync(order.Id)).ShouldBe((DeleteOrderStatus.NotFound, null));
        }

        await using var assert = Db.CreateContext();
        (await assert.Orders.SingleAsync(Ct)).Id.ShouldBe(other.Id);
        (await assert.OrderItems.SingleAsync(Ct)).OrderId.ShouldBe(other.Id);
        (await assert.OrderItemBatches.CountAsync(Ct)).ShouldBe(1);
    }

    // An invoiced order is reported, not deleted; its items stay intact.
    [Fact]
    public async Task DeleteOrderAsync_InvoicedOrder_ReturnsHasInvoiceWithoutDeleting()
    {
        var order = await SeedOrderAsync(2);
        await using (var seed = Db.CreateContext())
        {
            seed.Invoices.Add(
                new Invoice
                {
                    OrderId = order.Id,
                    Order = await seed.Orders.SingleAsync(Ct),
                    InvoiceStatus = "Draft",
                }
            );
            await seed.SaveChangesAsync(Ct);
        }

        await using (var context = Db.CreateContext())
        {
            var (status, returned) = await new OrderRepo(context).DeleteOrderAsync(order.Id);
            status.ShouldBe(DeleteOrderStatus.HasInvoice);
            returned.ShouldNotBeNull().Invoice.ShouldNotBeNull();
        }

        await using var assert = Db.CreateContext();
        (await assert.Orders.AnyAsync(o => o.Id == order.Id, Ct)).ShouldBeTrue();
        (await assert.OrderItems.CountAsync(l => l.OrderId == order.Id, Ct)).ShouldBe(2);
        (await assert.OrderItemBatches.CountAsync(Ct)).ShouldBe(2);
    }
}
