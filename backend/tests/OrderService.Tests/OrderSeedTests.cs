using Microsoft.EntityFrameworkCore;
using OrderService.DataAccess;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class OrderSeedTests(MySqlFixture mySql) : OrderDbTestBase(mySql)
{
    [Fact]
    public async Task SeedOrdersAsync_SyncedSeedData_SeedsOrdersAndInvoicesOnce()
    {
        object[] synced = [.. SeedData.Customers(), .. SeedData.Items()];
        await SeedAsync(synced);

        for (var run = 0; run < 2; run++)
        {
            await using var context = Db.CreateContext();
            await PrepDB.SeedOrdersAsync(context, SeedData.OrdersFile);
        }

        await using var assert = Db.CreateContext();
        var orders = await assert
            .Orders.Include(o => o.Invoice)
            .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Item)
            .ToListAsync(Ct);
        var (expectedOrders, expectedInvoices) = SeedData.OrderCounts();
        orders.Count.ShouldBe(expectedOrders);
        orders.ShouldAllBe(o => o.CustomerId != null && o.OrderItems.Count > 0);
        var invoiced = orders.Where(o => o.Invoice != null).ToList();
        invoiced.Count.ShouldBe(expectedInvoices);
        foreach (var order in invoiced)
        {
            order.Invoice!.TotalAmount.ShouldBe(
                order.OrderItems.Sum(l => l.Item.Price!.Value * l.OrderItemQuantity)
            );
        }
    }

    [Fact]
    public async Task SeedOrdersAsync_CustomersAndItemsNotSynced_SeedsNothing()
    {
        await using (var context = Db.CreateContext())
        {
            await PrepDB.SeedOrdersAsync(context, SeedData.OrdersFile);
        }

        await using var assert = Db.CreateContext();
        (await assert.Orders.CountAsync(Ct)).ShouldBe(0);
    }
}
