using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using OrderService.Dtos;
using OrderService.Mappings;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

// The gRPC substitutes answer from the Customer and Inventory seed data, so every reference in
// the order seed file can be snapshotted at startup.
public abstract class SeedDataOrderApiFactory : OrderApiFactory
{
    protected SeedDataOrderApiFactory(MySqlFixture mySql)
        : base(mySql)
    {
        InventoryClient.Serves(SeedData.Catalog());
        foreach (var (id, customer) in SeedData.Customers())
        {
            CustomerClient.GetCustomerAsync(id).Returns(customer);
        }
    }
}

public sealed class DevelopmentSeedDataOrderApiFactory(MySqlFixture mySql)
    : SeedDataOrderApiFactory(mySql);

public sealed class ProductionSeedDataOrderApiFactory(MySqlFixture mySql)
    : SeedDataOrderApiFactory(mySql)
{
    protected override string HostEnvironment => Environments.Production;
}

[Trait("Category", "Integration")]
public class DevelopmentOrderSeedDataTests(DevelopmentSeedDataOrderApiFactory factory)
    : IClassFixture<DevelopmentSeedDataOrderApiFactory>
{
    [Fact]
    public async Task Development_SeedsOrdersAndInvoices()
    {
        using var client = factory.CreateClient();

        var page = await client.GetFromJsonAsync<OrderPageReadDto>(
            "/api/orders?pageSize=50",
            TestContext.Current.CancellationToken
        );

        var seeds = SeedData.Orders();
        var orders = page.ShouldNotBeNull().Orders.ToList();
        orders.Select(o => o.Id).ShouldBe(seeds.Select(s => s.Id), ignoreOrder: true);
        orders.ShouldAllBe(o => o.Customer != null && o.Items.Count > 0);
        foreach (var seed in seeds)
        {
            var order = orders.Single(o => o.Id == seed.Id);
            (order.Status, order.CompletedDate, order.CancelledDate).ShouldBe(
                (seed.Status, seed.CompletedDate, seed.CancelledDate),
                $"Order {seed.Id}"
            );
            (order.Invoice != null).ShouldBe(seed.Invoice != null, $"Order {seed.Id}");
        }
        orders.Where(o => o.Status == OrderStatus.Completed).ShouldAllBe(o => o.Invoice != null);
        orders
            .Where(o => o.Status is OrderStatus.InProcess or OrderStatus.Pending)
            .ShouldAllBe(o => o.Invoice == null);
        var invoiced = orders.Where(o => o.Invoice != null).ToList();
        invoiced.Count.ShouldBe(seeds.Count(s => s.Invoice != null));
        invoiced.ShouldAllBe(o => o.Invoice!.Gross == o.Totals.Gross);
        foreach (var seed in seeds.Where(s => s.Invoice != null))
        {
            var invoice = orders.Single(o => o.Id == seed.Id).Invoice!;
            (invoice.Number, invoice.InvoiceDate, invoice.DueDate, invoice.PaymentStatus).ShouldBe(
                (
                    seed.Invoice!.Number,
                    seed.Invoice.InvoiceDate,
                    seed.Invoice.DueDate,
                    seed.Invoice.PaymentStatus
                )
            );
        }
    }

    [Fact]
    public async Task Development_SeedsTheCompanyAsTheInvoicesSupplier()
    {
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;

        var settings = await client.GetFromJsonAsync<CompanySettingsReadDto>(
            "/api/companysettings",
            ct
        );
        var page = await client.GetFromJsonAsync<InvoicePageReadDto>(
            "/api/invoices?pageSize=50",
            ct
        );
        var paid = await client.GetFromJsonAsync<InvoicePageReadDto>(
            "/api/invoices?pageSize=50&paymentStatus=Paid",
            ct
        );

        settings.ShouldNotBeNull().ShouldMatch(SeedData.Company());
        var seeds = SeedData.Orders().Where(s => s.Invoice != null).ToList();
        var invoices = page.ShouldNotBeNull().Invoices.ToList();
        invoices
            .Select(i => i.Number)
            .ShouldBe(seeds.Select(s => s.Invoice!.Number), ignoreOrder: true);
        invoices.ShouldAllBe(i => i.Supplier == settings.ToSupplierDto());
        invoices.ShouldAllBe(i => i.OrderStatus != null && i.Items.Count > 0);
        foreach (var seed in seeds)
        {
            var invoice = invoices.Single(i => i.OrderId == seed.Id);
            invoice.PaidDate.ShouldBe(seed.Invoice!.PaidDate);
            invoice.TaxEventDate.ShouldBe(seed.Invoice.InvoiceDate);
        }
        paid.ShouldNotBeNull()
            .Invoices.Select(i => i.OrderId)
            .ShouldBe(
                seeds.Where(s => s.Invoice!.PaymentStatus == PaymentStatus.Paid).Select(s => s.Id),
                ignoreOrder: true
            );
    }
}

[Trait("Category", "Integration")]
public class ProductionOrderSeedDataTests(ProductionSeedDataOrderApiFactory factory)
    : IClassFixture<ProductionSeedDataOrderApiFactory>
{
    [Fact]
    public async Task Production_DoesNotSeedOrders()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}

// Production starts without company settings, so orders can't be completed until an
// administrator saves them.
[Trait("Category", "Integration")]
public class ProductionCompanySettingsTests(ProductionSeedDataOrderApiFactory factory)
    : IClassFixture<ProductionSeedDataOrderApiFactory>
{
    [Fact]
    public async Task CompletingAnOrder_NeedsTheCompanySettings()
    {
        using var client = factory.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        var seed = TestData.Faker.PickRandom(SeedData.Orders());
        var created = await client.PostAsJsonAsync(
            "/api/orders",
            OrderBuilder.CreateDto(seed.CustomerId, [.. seed.Items]),
            ct
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var order = (await created.Content.ReadFromJsonAsync<OrderReadDto>(ct)).ShouldNotBeNull();
        var complete = OrderBuilder.StatusDto(OrderStatus.Completed);

        (await client.GetAsync("/api/companysettings", ct)).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );
        var refused = await client.PostAsJsonAsync($"/api/orders/{order.Id}/status", complete, ct);
        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await refused.Content.ReadFromJsonAsync<ProblemDetails>(ct))
            .ShouldNotBeNull()
            .Title.ShouldBe("Company settings are missing");
        var unchanged = await client.GetFromJsonAsync<OrderReadDto>($"/api/orders/{order.Id}", ct);
        unchanged.ShouldNotBeNull().Status.ShouldBe(OrderStatus.InProcess);
        unchanged.Invoice.ShouldBeNull();
        (await client.GetAsync("/api/invoices", ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var settings = CompanySettingsBuilder.Dto() with { PaymentTermsDays = null };
        (await client.PutAsJsonAsync("/api/companysettings", settings, ct)).StatusCode.ShouldBe(
            HttpStatusCode.OK
        );
        var completed = await client.PostAsJsonAsync(
            $"/api/orders/{order.Id}/status",
            complete,
            ct
        );

        completed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var summary = (await completed.Content.ReadFromJsonAsync<OrderReadDto>(ct))
            .ShouldNotBeNull()
            .Invoice.ShouldNotBeNull();
        // The first invoice; without configured terms it's due in 14 days.
        summary.Number.ShouldBe("0000000001");
        summary.DueDate.ShouldBe(summary.InvoiceDate!.Value.Date.AddDays(14));
        var invoice = await client.GetFromJsonAsync<InvoiceReadDto>(
            $"/api/invoices/{summary.Id}",
            ct
        );
        invoice.ShouldNotBeNull().Supplier.ShouldBe(settings.ToEntity().ToSupplierDto());
    }
}
