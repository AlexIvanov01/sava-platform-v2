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

        var item = FefoAllocator.Suggest(product, 7, Nothing, Today);

        item.ProductId.ShouldBe(product.Id);
        item.SKU.ShouldBe(product.SKU);
        item.Allocated.ShouldBe(7);
        item.Shortage.ShouldBeFalse();
        item.ShortBy.ShouldBe(0);
        item.Batches.Select(b => (b.BatchId, b.Available, b.Quantity))
            .ShouldBe([(early.Id, 4, 4), (late.Id, 10, 3)]);
        item.Batches[0].Lot.ShouldBe(early.Lot);
        item.Batches[0].Location.ShouldBe(early.Location);
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

        var item = FefoAllocator.Suggest(product, 5, Nothing, Today);

        item.Batches.Select(b => (b.BatchId, b.Quantity))
            .ShouldBe([(lastDay.Id, 2), (noExpiry.Id, 3)]);
    }

    [Fact]
    public void Suggest_NotEnoughStock_ReportsTheShortage()
    {
        var a = Batch(3, expiresInDays: 10);
        var b = Batch(2, expiresInDays: 20);
        var product = InventoryBuilder.Product(null, a, b, Batch(100, expiresInDays: -5));

        var item = FefoAllocator.Suggest(product, 9, Nothing, Today);

        item.Quantity.ShouldBe(9);
        item.Allocated.ShouldBe(5);
        item.Shortage.ShouldBeTrue();
        item.ShortBy.ShouldBe(4);
        item.Batches.Sum(x => x.Quantity).ShouldBe(5);
    }

    [Fact]
    public void Suggest_NoBatches_IsShortByEverything()
    {
        var item = FefoAllocator.Suggest(InventoryBuilder.Product(), 6, Nothing, Today);

        item.Batches.ShouldBeEmpty();
        item.ShortBy.ShouldBe(6);
        item.Shortage.ShouldBeTrue();
    }

    [Fact]
    public void Suggest_StockTheOrderAlreadyHoldsCountsAsAvailable()
    {
        // The order being edited already took the whole first batch (now 0 left).
        var held = Batch(0, expiresInDays: 5);
        var other = Batch(10, expiresInDays: 30);
        var product = InventoryBuilder.Product(null, held, other);

        var item = FefoAllocator.Suggest(
            product,
            6,
            new Dictionary<Guid, int> { [held.Id] = 4 },
            Today
        );

        item.Batches.Select(b => (b.BatchId, b.Available, b.Quantity))
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
