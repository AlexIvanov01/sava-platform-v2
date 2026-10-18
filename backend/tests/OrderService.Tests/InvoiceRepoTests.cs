using Microsoft.EntityFrameworkCore;
using OrderService.DataAccess;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class InvoiceRepoTests(MySqlFixture mySql) : OrderDbTestBase(mySql)
{
    private static Invoice NewInvoice(string status = "Draft") =>
        new()
        {
            InvoiceDate = new DateTime(2025, 3, 2, 0, 0, 0, DateTimeKind.Unspecified),
            InvoiceStatus = status,
            TotalAmount = 25m,
        };

    private async Task<(Order Order, Invoice Invoice)> SeedInvoicedOrderAsync()
    {
        var (_, _, order) = await SeedOrderAsync(1);
        await using var context = Db.CreateContext();
        var invoice = (
            await new InvoiceRepo(context).CreateInvoiceAsync(NewInvoice(), order.Id)
        ).ShouldNotBeNull();
        return (order, invoice);
    }

    [Fact]
    public async Task CreateInvoiceAsync_LinksToOrder_AndRejectsSecondInvoiceOrUnknownOrder()
    {
        var (order, invoice) = await SeedInvoicedOrderAsync();

        invoice.Id.ShouldBeGreaterThan(0);
        invoice.OrderId.ShouldBe(order.Id);
        await using var context = Db.CreateContext();
        var repo = new InvoiceRepo(context);
        (await repo.CreateInvoiceAsync(NewInvoice(), order.Id)).ShouldBeNull();
        (await repo.CreateInvoiceAsync(NewInvoice(), Guid.NewGuid())).ShouldBeNull();
        (await repo.GetInvoiceCountAsync()).ShouldBe(1);
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
            loaded.Order.Customer.ShouldNotBeNull();
            loaded.Order.OrderItems.ShouldHaveSingleItem().Item.Name.ShouldNotBeNull();
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
        first[0].Order.OrderItems.ShouldNotBeEmpty();
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
        saved.TotalAmount.ShouldBe(25m);
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
    public async Task DeleteInvoiceAsync_RemovesInvoiceOnly()
    {
        var (order, invoice) = await SeedInvoicedOrderAsync();

        await using (var context = Db.CreateContext())
        {
            var repo = new InvoiceRepo(context);
            (await repo.DeleteInvoiceAsync(invoice.Id)).ShouldNotBeNull();
            (await repo.DeleteInvoiceAsync(invoice.Id)).ShouldBeNull();
        }

        await using var assert = Db.CreateContext();
        (await assert.Invoices.AnyAsync(Ct)).ShouldBeFalse();
        (await assert.Orders.AnyAsync(o => o.Id == order.Id, Ct)).ShouldBeTrue();
    }
}
