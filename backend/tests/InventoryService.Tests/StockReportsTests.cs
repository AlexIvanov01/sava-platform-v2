using InventoryService.Dtos;
using InventoryService.Models;
using InventoryService.Reports;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

public class StockReportsTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(TestData.Date(2026, 2028));

    private static ProductBatch Batch(
        int quantity,
        int expiresInDays,
        decimal purchasePrice = 1m,
        string? lot = null
    )
    {
        var batch = InventoryBuilder.Batch(lot, quantity);
        batch.PurchasePrice = purchasePrice;
        batch.ExpirationDate = Today.AddDays(expiresInDays).ToDateTime(TimeOnly.MinValue);
        return batch;
    }

    private static Product Product(int? reorderLevel, string status, params ProductBatch[] batches)
    {
        var product = InventoryBuilder.Product(null, batches);
        product.ReorderLevel = reorderLevel;
        product.Status = status;
        foreach (var batch in batches)
        {
            batch.Product = product;
        }
        return product;
    }

    [Fact]
    public void LowStock_ListsActiveProductsAtOrBelowReorderLevelByShortfall()
    {
        var atLevel = Product(10, "Active", Batch(10, 30));
        var farBelow = Product(50, "Active", Batch(5, 30), Batch(100, -1)); // expired stock doesn't count
        var above = Product(10, "Active", Batch(11, 30));
        var inactive = Product(10, StockReports.InactiveStatus, Batch(0, 30));
        var noLevel = Product(null, "Active");

        var report = StockReports.LowStock([atLevel, farBelow, above, inactive, noLevel], Today);

        report
            .Select(r => (r.ProductId, r.AvailableQuantity, r.Shortfall))
            .ShouldBe([(farBelow.Id, 5, 45), (atLevel.Id, 10, 0)]);
    }

    [Fact]
    public void LowStock_OversoldProductHasShortfallBeyondItsLevel()
    {
        var oversold = Product(10, "Active", Batch(-4, 30));

        StockReports.LowStock([oversold], Today).ShouldHaveSingleItem().Shortfall.ShouldBe(14);
    }

    [Fact]
    public void Expiring_ListsBatchesWithStockInTheWindowSoonestFirst()
    {
        var today = Batch(3, 0, 2m, "TODAY");
        var inWindow = Batch(5, 7, 1.5m, "WEEK");
        var lastDay = Batch(1, 30, 1m, "LAST");
        var later = Batch(9, 31, 1m, "LATER");
        var empty = Batch(0, 3, 1m, "EMPTY");
        var expired = Batch(4, -2, 1m, "EXPIRED");
        var noDate = Batch(4, 0, 1m, "NODATE");
        noDate.ExpirationDate = null;
        Product(null, "Active", today, inWindow, lastDay, later, empty, expired, noDate);
        ProductBatch[] batches = [later, lastDay, expired, inWindow, empty, today, noDate];

        var report = StockReports.Expiring(batches, Today, 30, includeExpired: false);

        report
            .Select(r => (r.Lot, r.DaysUntilExpiry))
            .ShouldBe([("TODAY", 0), ("WEEK", 7), ("LAST", 30)]);
        report[1].ValueAtCost.ShouldBe(7.5m);
        StockReports
            .Expiring(batches, Today, 30, includeExpired: true)[0]
            .ShouldSatisfyAllConditions(
                r => r.Lot.ShouldBe("EXPIRED"),
                r => r.DaysUntilExpiry.ShouldBe(-2)
            );
    }

    [Fact]
    public void Valuation_ValuesEachBatchAtItsOwnPriceAndSeparatesExpired()
    {
        var a = Product(
            null,
            "Active",
            Batch(10, 30, 2.00m),
            Batch(5, 60, 3.10m),
            Batch(-3, 60, 9.99m), // oversold: not on the shelf
            Batch(4, -1, 1.25m) // expired
        );
        var b = Product(null, "Active", Batch(1, 30, 100m));
        var empty = Product(null, "Active", Batch(0, 30, 5m));

        var report = StockReports.Valuation([a, b, empty], Today);

        report.AsOf.ShouldBe(Today);
        report.Products.Select(p => p.ProductId).ShouldBe([b.Id, a.Id]);
        var rowA = report.Products.Single(p => p.ProductId == a.Id);
        rowA.Quantity.ShouldBe(15);
        rowA.ValueAtCost.ShouldBe(35.50m);
        rowA.ExpiredQuantity.ShouldBe(4);
        rowA.ExpiredValueAtCost.ShouldBe(5.00m);
        report.Quantity.ShouldBe(16);
        report.ValueAtCost.ShouldBe(135.50m);
        report.ExpiredQuantity.ShouldBe(4);
        report.ExpiredValueAtCost.ShouldBe(5.00m);
    }
}
