using Microsoft.EntityFrameworkCore;
using OrderService.DataAccess;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class InvoiceRepoTests(MySqlFixture mySql) : OrderDbTestBase(mySql)
{
    // A completed order with its invoice, as POST /api/Orders/{id}/status creates them.
    private async Task<(Order Order, Invoice Invoice)> SeedInvoicedOrderAsync(
        OrderStatus status = OrderStatus.Completed
    )
    {
        var order = OrderBuilder.Order(OrderBuilder.Item());
        order.Status = status;
        order.CompletedDate = TestData.Date(2025, 2026);
        if (status == OrderStatus.Cancelled)
        {
            order.CancelledDate = TestData.Date(2025, 2026);
        }
        var invoice = new Invoice
        {
            Order = order,
            OrderId = order.Id,
            InvoiceDate = order.CompletedDate,
            InvoiceStatus = "Unpaid",
            TotalAmount = order.Items.Gross(),
        };
        order.Invoice = invoice;
        await SeedAsync(order);
        return (order, invoice);
    }

    [Fact]
    public async Task SecondInvoiceForAnOrder_IsRejectedByTheDatabase()
    {
        var (order, invoice) = await SeedInvoicedOrderAsync();

        invoice.Id.ShouldBeGreaterThan(0);
        await using var context = Db.CreateContext();
        context.Invoices.Add(
            new Invoice
            {
                OrderId = order.Id,
                Order = await context.Orders.SingleAsync(Ct),
                InvoiceStatus = "Unpaid",
            }
        );
        await Should.ThrowAsync<DbUpdateException>(context.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task GetInvoice_ByIdAndOrderId_IncludesOrderGraph()
    {
        var (order, invoice) = await SeedInvoicedOrderAsync();

        await using var context = Db.CreateContext();
        var repo = new InvoiceRepo(context);
        foreach (
            var loaded in new[]
            {
                await repo.GetInvoiceByIdAsync(invoice.Id),
                await repo.GetInvoiceByOrderIdAsync(order.Id),
            }
        )
        {
            loaded.ShouldNotBeNull().Order.Id.ShouldBe(order.Id);
            loaded.Order.Customer.ShouldNotBeNull().Name.ShouldBe(order.Customer!.Name);
            var item = loaded.Order.Items.ShouldHaveSingleItem();
            item.Name.ShouldBe(order.Items[0].Name);
            item.Batches.ShouldHaveSingleItem().BatchId.ShouldBe(order.Items[0].Batches[0].BatchId);
        }
        (await repo.GetInvoiceByIdAsync(invoice.Id + 1000)).ShouldBeNull();
    }

    [Fact]
    public async Task GetAllInvoicesAsync_PagesByIdCursor()
    {
        var ids = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            ids.Add((await SeedInvoicedOrderAsync()).Invoice.Id);
        }

        await using var context = Db.CreateContext();
        var repo = new InvoiceRepo(context);
        var first = (await repo.GetAllInvoicesAsync(null, 2)).ToList();
        var second = (await repo.GetAllInvoicesAsync(first[^1].Id, 2)).ToList();

        first.Select(i => i.Id).ShouldBe(ids.Take(2));
        second.ShouldHaveSingleItem().Id.ShouldBe(ids[2]);
        first[0].Order.Items.ShouldHaveSingleItem().Batches.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task UpdateInvoiceAsync_UpdatesOnlySuppliedFields()
    {
        var (order, invoice) = await SeedInvoicedOrderAsync();
        var update = new Invoice { Id = invoice.Id, InvoiceStatus = "Paid" };

        await using (var context = Db.CreateContext())
        {
            (await new InvoiceRepo(context).UpdateInvoiceAsync(update)).ShouldNotBeNull();
        }

        await using var assert = Db.CreateContext();
        var saved = await assert.Invoices.SingleAsync(Ct);
        saved.InvoiceStatus.ShouldBe("Paid");
        saved.TotalAmount.ShouldBe(invoice.TotalAmount);
        saved.InvoiceDate.ShouldBe(invoice.InvoiceDate);
        saved.OrderId.ShouldBe(order.Id);
        (await assert.Orders.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task UpdateInvoiceAsync_Unknown_ReturnsNull()
    {
        await using var context = Db.CreateContext();

        (
            await new InvoiceRepo(context).UpdateInvoiceAsync(
                new Invoice { Id = 999, InvoiceStatus = "Paid" }
            )
        ).ShouldBeNull();
    }

    [Fact]
    public async Task DeleteInvoiceAsync_CompletedOrder_RemovesInvoiceAndMovesOrderToPending()
    {
        var (order, invoice) = await SeedInvoicedOrderAsync();
        var (other, otherInvoice) = await SeedInvoicedOrderAsync();

        await using (var context = Db.CreateContext())
        {
            var repo = new InvoiceRepo(context);
            (await repo.DeleteInvoiceAsync(invoice.Id)).ShouldNotBeNull().Id.ShouldBe(invoice.Id);
            (await repo.DeleteInvoiceAsync(invoice.Id)).ShouldBeNull();
        }

        await using var assert = Db.CreateContext();
        (await assert.Invoices.SingleAsync(Ct)).Id.ShouldBe(otherInvoice.Id);
        var saved = await assert
            .Orders.Include(o => o.Items)
            .SingleAsync(o => o.Id == order.Id, Ct);
        saved.Status.ShouldBe(OrderStatus.Pending);
        saved.CompletedDate.ShouldBeNull();
        saved.CancelledDate.ShouldBeNull();
        saved.UpdatedAt.ShouldNotBeNull();
        saved.Items.ShouldHaveSingleItem().ProductId.ShouldBe(order.Items[0].ProductId);
        // Other orders are untouched.
        var untouched = await assert.Orders.SingleAsync(o => o.Id == other.Id, Ct);
        untouched.Status.ShouldBe(OrderStatus.Completed);
        untouched.CompletedDate.ShouldBe(other.CompletedDate);
    }

    [Fact]
    public async Task DeleteInvoiceAsync_CancelledOrder_StaysCancelled()
    {
        var (order, invoice) = await SeedInvoicedOrderAsync(OrderStatus.Cancelled);

        await using (var context = Db.CreateContext())
        {
            (await new InvoiceRepo(context).DeleteInvoiceAsync(invoice.Id)).ShouldNotBeNull();
        }

        await using var assert = Db.CreateContext();
        (await assert.Invoices.AnyAsync(Ct)).ShouldBeFalse();
        var saved = await assert.Orders.SingleAsync(Ct);
        saved.Status.ShouldBe(OrderStatus.Cancelled);
        saved.CancelledDate.ShouldBe(order.CancelledDate);
        saved.CompletedDate.ShouldBe(order.CompletedDate);
    }

    [Fact]
    public async Task DeleteInvoiceAsync_Unknown_ReturnsNull()
    {
        var (order, invoice) = await SeedInvoicedOrderAsync();

        await using (var context = Db.CreateContext())
        {
            (await new InvoiceRepo(context).DeleteInvoiceAsync(invoice.Id + 1000)).ShouldBeNull();
        }

        await using var assert = Db.CreateContext();
        (await assert.Invoices.SingleAsync(Ct)).OrderId.ShouldBe(order.Id);
        (await assert.Orders.SingleAsync(Ct)).Status.ShouldBe(OrderStatus.Completed);
    }

    [Fact]
    public async Task GetInvoiceCountAsync_CountsInvoices()
    {
        await SeedInvoicedOrderAsync();
        await SeedInvoicedOrderAsync(OrderStatus.Cancelled);
        await SeedOrderAsync();

        await using var context = Db.CreateContext();
        (await new InvoiceRepo(context).GetInvoiceCountAsync()).ShouldBe(2);
    }
}
