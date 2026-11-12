using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.Models;
using OrderService.Reporting;
using OrderService.SyncDataServices.Grpc;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class SalesReportTests
{
    [Fact]
    public void ByCustomer_SumsInvoiceItemsAndCountsOrders()
    {
        var alice = TestData.Faker.Random.Guid();
        var bob = TestData.Faker.Random.Guid();
        var big = Completed(alice, Sold(quantity: 10, price: 10m, vat: 20m, cost: 6m));
        var small = Completed(alice, Sold(quantity: 1, price: 5m, vat: 9m, cost: 2m));
        var other = Completed(bob, Sold(quantity: 2, price: 3m, vat: 20m, cost: 1m));

        var report = SalesReport.Build(
            [small, other, big],
            SalesGrouping.Customer,
            null,
            null,
            null
        );

        report.Rows.Select(r => r.Key).ShouldBe([alice.ToString(), bob.ToString()]);
        var first = report.Rows[0];
        var latest = Latest(big, small).Invoice!.Customer;
        first.Label.ShouldBe(latest.CompanyName ?? latest.Name);
        first.OrderCount.ShouldBe(2);
        first.Quantity.ShouldBeNull();
        (first.Net, first.Vat, first.Gross).ShouldBe((105m, 20.45m, 125.45m));
        (first.Cost, first.Ebit, first.EbitPercent).ShouldBe((62m, 43m, 40.95m));
        first.NetWithoutCost.ShouldBe(0m);
        var totals = report.Totals;
        (totals.Key, totals.OrderCount, totals.Net, totals.Cost).ShouldBe(("total", 3, 111m, 64m));
    }

    [Fact]
    public void ByProduct_SumsQuantitiesAndFiltersOneProduct()
    {
        var product = OrderBuilder.Product();
        var a = Completed(
            null,
            Sold(product, quantity: 3, price: 4m),
            Sold(quantity: 1, price: 50m)
        );
        var b = Completed(null, Sold(product, quantity: 2, price: 4m));

        var all = SalesReport.Build([a, b], SalesGrouping.Product, null, null, null);
        var one = SalesReport.Build([a, b], SalesGrouping.Product, null, null, product.Id);

        all.Rows.Count.ShouldBe(2);
        all.Rows[0].Net.ShouldBe(50m);
        var row = one.Rows.ShouldHaveSingleItem();
        row.Key.ShouldBe(product.Id.ToString());
        row.Label.ShouldBe($"{product.Name} ({product.SKU})");
        (row.Quantity, row.OrderCount, row.Net).ShouldBe((5, 2, 20m));
        one.Totals.Quantity.ShouldBe(5);
    }

    [Fact]
    public void ByMonth_UsesTheInvoiceDateInOrder()
    {
        var march = Completed(null, new DateTime(2026, 3, 31, 23, 0, 0, DateTimeKind.Utc), Sold());
        var january = Completed(null, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), Sold());

        var report = SalesReport.Build([march, january], SalesGrouping.Month, null, null, null);

        report
            .Rows.Select(r => (r.Key, r.Label))
            .ShouldBe([("2026-01", "2026-01"), ("2026-03", "2026-03")]);
    }

    [Fact]
    public void ItemsWithoutCost_AreLeftOutOfEbit()
    {
        var order = Completed(
            null,
            Sold(quantity: 2, price: 10m, cost: 4m),
            Sold(quantity: 1, price: 30m, cost: null)
        );

        var row = SalesReport.Build([order], SalesGrouping.Customer, null, null, null).Totals;

        (row.Net, row.Cost, row.Ebit, row.EbitPercent, row.NetWithoutCost).ShouldBe(
            (50m, 8m, 12m, 60m, 30m)
        );
    }

    [Fact]
    public void NoCostAtAll_HasNoEbitPercent()
    {
        var order = Completed(null, Sold(quantity: 1, price: 10m, cost: null));

        var row = SalesReport.Build([order], SalesGrouping.Customer, null, null, null).Totals;

        (row.Cost, row.Ebit, row.EbitPercent, row.NetWithoutCost).ShouldBe((0m, 0m, null, 10m));
    }

    [Fact]
    public void OrdersWithoutInvoice_AndNoOrders_GiveEmptyRows()
    {
        var uninvoiced = OrderBuilder.Order(Sold());

        var report = SalesReport.Build([uninvoiced], SalesGrouping.Product, null, null, null);

        report.Rows.ShouldBeEmpty();
        (report.Totals.Net, report.Totals.OrderCount, report.Totals.Quantity).ShouldBe((0m, 0, 0));
    }

    internal static OrderItem Sold(
        ProductSnapshot? product = null,
        int quantity = 1,
        decimal price = 10m,
        decimal vat = 20m,
        decimal? cost = 5m
    )
    {
        var p = (product ?? OrderBuilder.Product()) with
        {
            ListPrice = price,
            VatRate = vat,
            Cost = cost,
        };
        return OrderBuilder.Item(p, (OrderBuilder.Batch(p.Id), quantity));
    }

    internal static Order Completed(Guid? customerId, params OrderItem[] items) =>
        Completed(customerId, TestData.Date(2025, 2026), items);

    internal static Order Completed(
        Guid? customerId,
        DateTime invoiceDate,
        params OrderItem[] items
    )
    {
        var order = OrderBuilder.OrderFor(customerId ?? TestData.Faker.Random.Guid(), items);
        order.Status = OrderStatus.Completed;
        OrderBuilder.Invoice(order, date: invoiceDate);
        return order;
    }

    private static Order Latest(params Order[] orders) =>
        orders.MaxBy(o => o.Invoice!.InvoiceDate)!;
}

public class SalesReportRepoTests(MySqlFixture mySql) : OrderDbTestBase(mySql)
{
    [Fact]
    public async Task CompletedOrders_FiltersStatusInvoiceDayAndCustomer()
    {
        var customer = TestData.Faker.Random.Guid();
        var firstDay = Completed(customer, new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var lastDay = Completed(customer, new DateTime(2026, 5, 31, 23, 59, 59, DateTimeKind.Utc));
        var nextDay = Completed(customer, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var otherCustomer = Completed(null, new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc));
        var cancelled = Completed(customer, new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc));
        cancelled.Status = OrderStatus.Cancelled;
        var open = OrderBuilder.OrderFor(customer, SalesReportTests.Sold());
        await SeedAsync(firstDay, lastDay, nextDay, otherCustomer, cancelled, open);

        await using var context = Db.CreateContext();
        var repo = new OrderRepo(context);
        var may = await repo.GetCompletedOrdersAsync(new(2026, 5, 1), new(2026, 5, 31), null);
        var mine = await repo.GetCompletedOrdersAsync(null, null, customer);

        may.Select(o => o.Id)
            .ShouldBe([firstDay.Id, lastDay.Id, otherCustomer.Id], ignoreOrder: true);
        mine.Select(o => o.Id).ShouldBe([firstDay.Id, lastDay.Id, nextDay.Id], ignoreOrder: true);
        var loaded = mine.Single(o => o.Id == firstDay.Id);
        loaded.Items.ShouldHaveSingleItem().Cost.ShouldBe(firstDay.Items[0].Cost);
        loaded.Invoice.ShouldNotBeNull().Items.ShouldHaveSingleItem();
    }

    private static Order Completed(Guid? customer, DateTime invoiceDate) =>
        SalesReportTests.Completed(customer, invoiceDate, SalesReportTests.Sold());
}

[Trait("Category", "Integration")]
public class SalesReportApiTests(DevelopmentSeedDataOrderApiFactory factory)
    : IClassFixture<DevelopmentSeedDataOrderApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Sales_CoverTheSeededCompletedOrders()
    {
        var completed = (
            await _client.GetFromJsonAsync<OrderPageReadDto>(
                "/api/orders?status=Completed&pageSize=50",
                Ct
            )
        )
            .ShouldNotBeNull()
            .Orders.ToList();

        var report = await _client.GetFromJsonAsync<SalesReportDto>(
            "/api/reports/sales?groupBy=month",
            Ct
        );

        report.ShouldNotBeNull().GroupBy.ShouldBe(SalesGrouping.Month);
        report.Totals.OrderCount.ShouldBe(completed.Count);
        report.Totals.Gross.ShouldBe(completed.Sum(o => o.Invoice!.Gross));
        report
            .Rows.Select(r => r.Key)
            .ShouldBe(
                completed
                    .Select(o =>
                        o.Invoice!.InvoiceDate!.Value.ToString(
                            "yyyy-MM",
                            CultureInfo.InvariantCulture
                        )
                    )
                    .Distinct()
                    .Order(StringComparer.Ordinal)
            );
        report.Rows.Sum(r => r.Net).ShouldBe(report.Totals.Net);
    }

    [Fact]
    public async Task Sales_FilterByPeriodAndCustomer()
    {
        var completed = (
            await _client.GetFromJsonAsync<OrderPageReadDto>(
                "/api/orders?status=Completed&pageSize=50",
                Ct
            )
        )
            .ShouldNotBeNull()
            .Orders.ToList();
        var order = completed[0];
        var day = DateOnly
            .FromDateTime(order.Invoice!.InvoiceDate!.Value)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var report = await _client.GetFromJsonAsync<SalesReportDto>(
            $"/api/reports/sales?from={day}&to={day}&customerId={order.CustomerId}&groupBy=Product",
            Ct
        );

        report.ShouldNotBeNull().Totals.OrderCount.ShouldBe(1);
        report.Totals.Net.ShouldBe(order.Totals.Net);
        report.Rows.Count.ShouldBe(order.Items.Count);
    }

    [Fact]
    public async Task Sales_FromAfterTo_Returns400()
    {
        var response = await _client.GetAsync(
            "/api/reports/sales?from=2026-02-01&to=2026-01-01",
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
