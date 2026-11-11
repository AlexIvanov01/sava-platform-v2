using System.Net;
using System.Net.Http.Json;
using InventoryService.Dtos;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

// The development seed on a fixed day, so the reports over it are deterministic.
public sealed class ReportsApiFactory(MySqlFixture mySql) : InventoryApiFactory(mySql)
{
    public static readonly DateOnly Today = new(2026, 10, 20);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(
                new FakeTimeProvider(
                    new DateTimeOffset(Today.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero)
                )
            );
        });
    }
}

[Trait("Category", "Integration")]
public class ReportsApiTests(ReportsApiFactory factory) : IClassFixture<ReportsApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LowStock_ListsSeededProductsBelowTheirReorderLevel()
    {
        var report = await _client.GetFromJsonAsync<List<LowStockItemDto>>(
            "/api/reports/low-stock",
            Ct
        );

        report
            .ShouldNotBeNull()
            .Select(r => (r.SKU, r.AvailableQuantity, r.ReorderLevel, r.Shortfall))
            .ShouldBe([("DEL-001", 22, 30, 8), ("COS-001", 18, 25, 7), ("HOU-002", 35, 40, 5)]);
    }

    [Fact]
    public async Task Expiring_ListsSeededBatchesInTheWindow()
    {
        var report = await _client.GetFromJsonAsync<List<ExpiringBatchDto>>(
            "/api/reports/expiring?days=30",
            Ct
        );

        report
            .ShouldNotBeNull()
            .Select(r => (r.Lot, r.DaysUntilExpiry))
            .ShouldBe([("YOG-2610A", 10), ("FET-2608", 16), ("YOG-2610B", 23)]);
        report.Single(r => r.Lot == "FET-2608").ValueAtCost.ShouldBe(37.20m);
    }

    [Fact]
    public async Task Expiring_IncludeExpired_AddsExpiredBatchesWithStock()
    {
        var report = await _client.GetFromJsonAsync<List<ExpiringBatchDto>>(
            "/api/reports/expiring?days=0&includeExpired=true",
            Ct
        );

        report
            .ShouldNotBeNull()
            .Select(r => (r.SKU, r.Lot, r.DaysUntilExpiry))
            .ShouldBe([("PAN-003", "SUN-2512", -112), ("PAN-001", "LYU-2603", -20)]);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public async Task Expiring_DaysOutOfRange_Returns400(int days)
    {
        (await _client.GetAsync($"/api/reports/expiring?days={days}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.BadRequest
        );
    }

    [Fact]
    public async Task StockValuation_TotalsTheSeedAtPurchasePrice()
    {
        var report = (
            await _client.GetFromJsonAsync<StockValuationDto>("/api/reports/stock-valuation", Ct)
        ).ShouldNotBeNull();

        report.AsOf.ShouldBe(ReportsApiFactory.Today);
        report.Quantity.ShouldBe(1558);
        report.ValueAtCost.ShouldBe(4301.85m);
        report.ExpiredQuantity.ShouldBe(38);
        report.ExpiredValueAtCost.ShouldBe(52.10m);
        report.Products.Sum(p => p.ValueAtCost).ShouldBe(report.ValueAtCost);
    }

    [Fact]
    public async Task StockValuation_FiltersByCategory()
    {
        var dairy = (await _client.GetFromJsonAsync<List<CategoryReadDto>>("/api/categories", Ct))
            .ShouldNotBeNull()
            .Single(c => c.Name == "Dairy");

        var report = (
            await _client.GetFromJsonAsync<StockValuationDto>(
                $"/api/reports/stock-valuation?categoryId={dairy.Id}",
                Ct
            )
        ).ShouldNotBeNull();

        report.Products.Count.ShouldBe(3);
        report.Products.ShouldAllBe(p => p.CategoryId == dairy.Id && p.CategoryName == "Dairy");
    }
}
