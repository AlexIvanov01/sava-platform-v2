using Microsoft.EntityFrameworkCore;
using OrderService.DataAccess;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class OrderRepoTests(MySqlFixture mySql) : OrderDbTestBase(mySql)
{
    [Fact]
    public async Task Migrations_MatchTheModel()
    {
        await using var context = Db.CreateContext();

        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task CreateOrderAsync_ValidOrder_PersistsWithItemsAndCustomer()
    {
        var customer = OrderBuilder.Customer();
        var item = OrderBuilder.Item();
        await SeedAsync(customer, item);
        var order = OrderBuilder
            .CreateDto(customer.ExternalId, (item.ExternalBatchId, 3))
            .ToEntityForTest();

        Order? created;
        await using (var context = Db.CreateContext())
        {
            created = await new OrderRepo(context).CreateOrderAsync(order);
        }

        created
            .ShouldNotBeNull()
            .Customer.ShouldNotBeNull()
            .ExternalId.ShouldBe(customer.ExternalId);
        await using var assert = Db.CreateContext();
        var saved = (await new OrderRepo(assert).GetOrderByIdAsync(created.Id)).ShouldNotBeNull();
        saved.Customer.ShouldNotBeNull();
        var line = saved.OrderItems.ShouldHaveSingleItem();
        line.Item.Name.ShouldBe(item.Name);
        line.OrderItemQuantity.ShouldBe(3);
        (await assert.Items.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task CreateOrderAsync_InvalidInput_ReturnsNullAndPersistsNothing()
    {
        var customer = OrderBuilder.Customer();
        var item = OrderBuilder.Item();
        await SeedAsync(customer, item);

        await using var context = Db.CreateContext();
        var repo = new OrderRepo(context);
        (
            await repo.CreateOrderAsync(
                OrderBuilder.CreateDto(customer.ExternalId).ToEntityForTest()
            )
        ).ShouldBeNull();
        (
            await repo.CreateOrderAsync(
                OrderBuilder.CreateDto(Guid.NewGuid(), (item.ExternalBatchId, 1)).ToEntityForTest()
            )
        ).ShouldBeNull();
        (
            await repo.CreateOrderAsync(
                OrderBuilder.CreateDto(customer.ExternalId, (Guid.NewGuid(), 1)).ToEntityForTest()
            )
        ).ShouldBeNull();
        (
            await repo.CreateOrderAsync(
                OrderBuilder
                    .CreateDto(
                        customer.ExternalId,
                        (item.ExternalBatchId, 1),
                        (item.ExternalBatchId, 2)
                    )
                    .ToEntityForTest()
            )
        ).ShouldBeNull();
        (await context.Orders.CountAsync(Ct)).ShouldBe(0);
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
        first[0].OrderItems.ShouldHaveSingleItem().Item.ShouldNotBeNull();
        second.ShouldHaveSingleItem();
        first.Select(o => o.Id).ShouldNotContain(second[0].Id);
        (await repo.GetOrderCountAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task GetAllOrdersByCustomerIdAsync_FiltersAndPages()
    {
        var customer = OrderBuilder.Customer();
        var item = OrderBuilder.Item();
        var orders = new[]
        {
            OrderBuilder.Order(customer, (item, 1)),
            OrderBuilder.Order(customer, (item, 2)),
        };
        await SeedAsync(customer, orders[0], orders[1]);
        await SeedOrderAsync();

        await using var context = Db.CreateContext();
        var repo = new OrderRepo(context);
        var first = (
            await repo.GetAllOrdersByCustomerIdAsync(customer.ExternalId, null, 1)
        ).ShouldHaveSingleItem();
        var second = (
            await repo.GetAllOrdersByCustomerIdAsync(customer.ExternalId, first.Id, 10)
        ).ShouldHaveSingleItem();

        new[] { first.Id, second.Id }.ShouldBe(orders.Select(o => o.Id), ignoreOrder: true);
    }

    [Fact]
    public async Task UpdateOrderAsync_UpdatesFieldsAndMergesItems()
    {
        var (_, items, order) = await SeedOrderAsync(2);
        var newItem = OrderBuilder.Item("New");
        await SeedAsync(newItem);
        var update = OrderBuilder
            .UpdateDto("Shipped", null, (items[1].ExternalBatchId, 7), (newItem.ExternalBatchId, 1))
            .ToEntityForTest(order.Id);

        await using (var context = Db.CreateContext())
        {
            var result = (await new OrderRepo(context).UpdateOrderAsync(update)).ShouldNotBeNull();

            result
                .PreviousItems.Select(oi => (oi.ItemId, oi.OrderItemQuantity))
                .ShouldBe(
                    [(items[0].ExternalBatchId, 1), (items[1].ExternalBatchId, 2)],
                    ignoreOrder: true
                );
        }

        await using var assert = Db.CreateContext();
        var saved = (await new OrderRepo(assert).GetOrderByIdAsync(order.Id)).ShouldNotBeNull();
        saved.OrderStatus.ShouldBe("Shipped");
        saved.ShippingAddress.ShouldBe(order.ShippingAddress);
        saved
            .OrderItems.Select(oi => (oi.ItemId, oi.OrderItemQuantity))
            .ShouldBe(
                [(items[1].ExternalBatchId, 7), (newItem.ExternalBatchId, 1)],
                ignoreOrder: true
            );
    }

    [Fact]
    public async Task UpdateOrderAsync_WithoutItems_KeepsItems()
    {
        var (_, _, order) = await SeedOrderAsync(2);

        await using (var context = Db.CreateContext())
        {
            (
                await new OrderRepo(context).UpdateOrderAsync(
                    OrderBuilder.UpdateDto("Paid").ToEntityForTest(order.Id)
                )
            ).ShouldNotBeNull();
        }

        await using var assert = Db.CreateContext();
        (await assert.OrderItems.CountAsync(oi => oi.OrderId == order.Id, Ct)).ShouldBe(2);
    }

    [Fact]
    public async Task UpdateOrderAsync_UnknownOrderCustomerOrItem_ReturnsNull()
    {
        var (_, _, order) = await SeedOrderAsync(1);

        await using var context = Db.CreateContext();
        var repo = new OrderRepo(context);
        (
            await repo.UpdateOrderAsync(OrderBuilder.UpdateDto("x").ToEntityForTest(Guid.NewGuid()))
        ).ShouldBeNull();
        (
            await repo.UpdateOrderAsync(
                OrderBuilder.UpdateDto(customerId: Guid.NewGuid()).ToEntityForTest(order.Id)
            )
        ).ShouldBeNull();
        (
            await repo.UpdateOrderAsync(
                OrderBuilder.UpdateDto(lines: (Guid.NewGuid(), 1)).ToEntityForTest(order.Id)
            )
        ).ShouldBeNull();
    }

    [Fact]
    public async Task DeleteOrderAsync_RemovesOrderAndItsLines_KeepsItems()
    {
        var (_, items, order) = await SeedOrderAsync(2);

        await using (var context = Db.CreateContext())
        {
            var (status, deleted) = await new OrderRepo(context).DeleteOrderAsync(order.Id);
            status.ShouldBe(DeleteOrderStatus.Deleted);
            deleted.ShouldNotBeNull().OrderItems.Count.ShouldBe(2);
            (await new OrderRepo(context).DeleteOrderAsync(order.Id)).ShouldBe(
                (DeleteOrderStatus.NotFound, null)
            );
        }

        await using var assert = Db.CreateContext();
        (await assert.Orders.AnyAsync(Ct)).ShouldBeFalse();
        (await assert.OrderItems.AnyAsync(Ct)).ShouldBeFalse();
        (await assert.Items.CountAsync(Ct)).ShouldBe(items.Length);
    }

    // An invoiced order is reported, not deleted; its lines stay intact.
    [Fact]
    public async Task DeleteOrderAsync_InvoicedOrder_ReturnsHasInvoiceWithoutDeleting()
    {
        var (_, _, order) = await SeedOrderAsync(2);
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
            var (status, _) = await new OrderRepo(context).DeleteOrderAsync(order.Id);
            status.ShouldBe(DeleteOrderStatus.HasInvoice);
        }

        await using var assert = Db.CreateContext();
        (await assert.Orders.AnyAsync(o => o.Id == order.Id, Ct)).ShouldBeTrue();
        (await assert.OrderItems.CountAsync(oi => oi.OrderId == order.Id, Ct)).ShouldBe(2);
    }
}
