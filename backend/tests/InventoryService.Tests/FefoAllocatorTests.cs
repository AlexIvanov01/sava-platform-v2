using InventoryService.Allocation;
using InventoryService.Models;

namespace InventoryService.Tests;

public class FefoAllocatorTests
{
    private static readonly DateOnly Today = new(2028, 6, 15);
    private static readonly Dictionary<Guid, int> Nothing = [];

    [Fact]
    public void Suggest_TakesEarliestExpiryFirstAndSplitsAcrossBatches()
    {
        var late = Batch(10, expiresInDays: 30);
        var early = Batch(4, expiresInDays: 5);
        var product = InventoryBuilder.Product(null, late, early);

        var line = FefoAllocator.Suggest(product, 7, Nothing, Today);

        line.ProductId.ShouldBe(product.Id);
        line.SKU.ShouldBe(product.SKU);
        line.Allocated.ShouldBe(7);
        line.Shortage.ShouldBeFalse();
        line.ShortBy.ShouldBe(0);
        line.Batches.Select(b => (b.BatchId, b.Available, b.Quantity))
            .ShouldBe([(early.Id, 4, 4), (late.Id, 10, 3)]);
        line.Batches[0].Lot.ShouldBe(early.Lot);
        line.Batches[0].Location.ShouldBe(early.Location);
    }

    [Fact]
    public void Suggest_SkipsExpiredAndEmptyBatchesAndUsesNonExpiringOnesLast()
    {
        var expired = Batch(50, expiresInDays: -1);
        var empty = Batch(0, expiresInDays: 1);
        var negative = Batch(-3, expiresInDays: 2);
        var lastDay = Batch(2, expiresInDays: 0);
        var noExpiry = Batch(20, expiresInDays: null);
        var product = InventoryBuilder.Product(null, noExpiry, expired, empty, negative, lastDay);

        var line = FefoAllocator.Suggest(product, 5, Nothing, Today);

        line.Batches.Select(b => (b.BatchId, b.Quantity))
            .ShouldBe([(lastDay.Id, 2), (noExpiry.Id, 3)]);
    }

    [Fact]
    public void Suggest_NotEnoughStock_ReportsTheShortage()
    {
        var a = Batch(3, expiresInDays: 10);
        var b = Batch(2, expiresInDays: 20);
        var product = InventoryBuilder.Product(null, a, b, Batch(100, expiresInDays: -5));

        var line = FefoAllocator.Suggest(product, 9, Nothing, Today);

        line.Quantity.ShouldBe(9);
        line.Allocated.ShouldBe(5);
        line.Shortage.ShouldBeTrue();
        line.ShortBy.ShouldBe(4);
        line.Batches.Sum(x => x.Quantity).ShouldBe(5);
    }

    [Fact]
    public void Suggest_NoBatches_IsShortByEverything()
    {
        var line = FefoAllocator.Suggest(InventoryBuilder.Product(), 6, Nothing, Today);

        line.Batches.ShouldBeEmpty();
        line.ShortBy.ShouldBe(6);
        line.Shortage.ShouldBeTrue();
    }

    [Fact]
    public void Suggest_StockTheOrderAlreadyHoldsCountsAsAvailable()
    {
        // The order being edited already took the whole first batch (now 0 left).
        var held = Batch(0, expiresInDays: 5);
        var other = Batch(10, expiresInDays: 30);
        var product = InventoryBuilder.Product(null, held, other);

        var line = FefoAllocator.Suggest(
            product,
            6,
            new Dictionary<Guid, int> { [held.Id] = 4 },
            Today
        );

        line.Batches.Select(b => (b.BatchId, b.Available, b.Quantity))
            .ShouldBe([(held.Id, 4, 4), (other.Id, 10, 2)]);
    }

    private static ProductBatch Batch(int quantity, int? expiresInDays)
    {
        var batch = InventoryBuilder.Batch(quantity: quantity);
        batch.ExpirationDate = expiresInDays is { } days
            ? Today.AddDays(days).ToDateTime(TimeOnly.MinValue)
            : null;
        return batch;
    }
}
