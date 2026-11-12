using Microsoft.EntityFrameworkCore;
using OrderService.DataAccess;
using OrderService.Dtos;
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
        saved.Status.ShouldBe(OrderStatus.InProcess);
        saved.CompletedDate.ShouldBeNull();
        saved.CancelledDate.ShouldBeNull();
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

    private static readonly OrderFilter NoFilter = new(null, null, null, null);

    private static Order OrderOn(DateTime orderDate, OrderStatus status = OrderStatus.InProcess)
    {
        var order = OrderBuilder.Order(OrderBuilder.Item());
        order.OrderDate = orderDate;
        order.Status = status;
        return order;
    }

    private async Task<List<Guid>> AllIdsAsync(OrderFilter filter)
    {
        await using var context = Db.CreateContext();
        var repo = new OrderRepo(context);
        var orders = (await repo.GetOrdersAsync(filter, null, 100)).ToList();
        (await repo.GetOrderCountAsync(filter)).ShouldBe(orders.Count);
        return [.. orders.Select(o => o.Id)];
    }

    [Fact]
    public async Task GetOrdersAsync_NoFilter_PagesByIdCursor()
    {
        for (var i = 0; i < 3; i++)
        {
            await SeedOrderAsync(1);
        }

        await using var context = Db.CreateContext();
        var repo = new OrderRepo(context);
        var first = (await repo.GetOrdersAsync(NoFilter, null, 2)).ToList();
        var second = (await repo.GetOrdersAsync(NoFilter, first[^1].Id, 2)).ToList();

        first.Count.ShouldBe(2);
        first[0].Customer.ShouldNotBeNull();
        first[0].Items.ShouldHaveSingleItem().Batches.ShouldHaveSingleItem();
        first.Select(o => o.Id).ShouldNotContain(second.ShouldHaveSingleItem().Id);
        (await repo.GetOrderCountAsync(NoFilter)).ShouldBe(3);
    }

    [Fact]
    public async Task GetOrdersAsync_ByCustomer_FiltersAndPages()
    {
        var customerId = TestData.Faker.Random.Guid();
        var orders = new[]
        {
            OrderBuilder.OrderFor(customerId, OrderBuilder.Item()),
            OrderBuilder.OrderFor(customerId, OrderBuilder.Item()),
        };
        await SeedAsync(orders[0], orders[1]);
        await SeedOrderAsync();
        var filter = new OrderFilter(null, customerId, null, null);

        await using var context = Db.CreateContext();
        var repo = new OrderRepo(context);
        var first = (await repo.GetOrdersAsync(filter, null, 1)).ShouldHaveSingleItem();
        var second = (await repo.GetOrdersAsync(filter, first.Id, 10)).ShouldHaveSingleItem();

        new[] { first.Id, second.Id }.ShouldBe(orders.Select(o => o.Id), ignoreOrder: true);
        second.Items.ShouldHaveSingleItem();
        (await repo.GetOrderCountAsync(filter)).ShouldBe(2);
    }

    [Fact]
    public async Task GetOrdersAsync_ByStatus_ReturnsOnlyThatStatus()
    {
        var byStatus = Enum.GetValues<OrderStatus>()
            .ToDictionary(
                status => status,
                status =>
                    Enumerable
                        .Range(0, TestData.Faker.Random.Int(1, 3))
                        .Select(_ => OrderOn(TestData.Date(2024, 2026), status))
                        .ToList()
            );
        await SeedAsync([.. byStatus.Values.SelectMany(o => o)]);

        foreach (var (status, orders) in byStatus)
        {
            (await AllIdsAsync(new OrderFilter(status, null, null, null))).ShouldBe(
                orders.Select(o => o.Id),
                ignoreOrder: true
            );
        }
        (await AllIdsAsync(NoFilter)).Count.ShouldBe(byStatus.Values.Sum(o => o.Count));
    }

    [Fact]
    public async Task GetOrdersAsync_FromTo_AreInclusiveDays()
    {
        var from = DateOnly.FromDateTime(TestData.Date(2024, 2025));
        var to = from.AddDays(TestData.Faker.Random.Int(0, 30));
        var start = from.ToDateTime(TimeOnly.MinValue);
        var end = to.ToDateTime(TimeOnly.MinValue);
        var beforeFrom = OrderOn(start.AddSeconds(-1));
        var atFrom = OrderOn(start);
        var lastSecondOfTo = OrderOn(end.AddDays(1).AddSeconds(-1));
        var dayAfterTo = OrderOn(end.AddDays(1));
        await SeedAsync(beforeFrom, atFrom, lastSecondOfTo, dayAfterTo);

        (await AllIdsAsync(new OrderFilter(null, null, from, to))).ShouldBe(
            [atFrom.Id, lastSecondOfTo.Id],
            ignoreOrder: true
        );
        (await AllIdsAsync(new OrderFilter(null, null, from, null))).ShouldBe(
            [atFrom.Id, lastSecondOfTo.Id, dayAfterTo.Id],
            ignoreOrder: true
        );
        (await AllIdsAsync(new OrderFilter(null, null, null, to))).ShouldBe(
            [beforeFrom.Id, atFrom.Id, lastSecondOfTo.Id],
            ignoreOrder: true
        );
        // A single day: 00:00:00 to 23:59:59.
        (await AllIdsAsync(new OrderFilter(null, null, to, to))).ShouldBe(
            from == to ? new[] { atFrom.Id, lastSecondOfTo.Id } : new[] { lastSecondOfTo.Id },
            ignoreOrder: true
        );
    }

    [Fact]
    public async Task GetOrdersAsync_CombinedFilters_MatchAll()
    {
        var customerId = TestData.Faker.Random.Guid();
        var day = DateOnly.FromDateTime(TestData.Date(2024, 2025));
        var at = day.ToDateTime(new TimeOnly(TestData.Faker.Random.Int(0, 23), 0));
        var match = OrderOn(at, OrderStatus.Pending);
        match.CustomerId = customerId;
        var otherStatus = OrderOn(at, OrderStatus.InProcess);
        otherStatus.CustomerId = customerId;
        var otherCustomer = OrderOn(at, OrderStatus.Pending);
        var otherDay = OrderOn(at.AddDays(1), OrderStatus.Pending);
        otherDay.CustomerId = customerId;
        await SeedAsync(match, otherStatus, otherCustomer, otherDay);

        var filter = new OrderFilter(OrderStatus.Pending, customerId, day, day);
        (await AllIdsAsync(filter)).ShouldBe([match.Id]);
        await using var context = Db.CreateContext();
        (await new OrderRepo(context).GetOrdersAsync(filter, match.Id, 10)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.InProcess)]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task Status_IsStoredAsItsName(OrderStatus status)
    {
        var order = OrderOn(TestData.Date(2024, 2026), status);
        order.CompletedDate = TestData.Date(2025, 2026);
        order.CancelledDate = TestData.Date(2025, 2026);
        await SeedAsync(order);

        await using var context = Db.CreateContext();
        var stored = await context
            .Database.SqlQuery<string>($"SELECT Status AS Value FROM Orders WHERE Id = {order.Id}")
            .SingleAsync(Ct);
        stored.ShouldBe(status.ToString());
        var saved = (await new OrderRepo(context).GetOrderByIdAsync(order.Id)).ShouldNotBeNull();
        saved.Status.ShouldBe(status);
        saved.CompletedDate.ShouldBe(order.CompletedDate);
        saved.CancelledDate.ShouldBe(order.CancelledDate);
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
                OrderBuilder.Invoice(await seed.Orders.Include(o => o.Items).SingleAsync(Ct))
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
