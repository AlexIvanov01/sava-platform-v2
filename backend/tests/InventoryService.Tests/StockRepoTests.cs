using InventoryService.DataAccess;
using InventoryService.Models;
using Microsoft.EntityFrameworkCore;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

[Trait("Category", "Integration")]
public class StockRepoTests(MySqlFixture mySql) : IAsyncLifetime
{
    private TestDatabase<ProductContext> _db = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        _db = await mySql.CreateDatabaseAsync(options => new ProductContext(options));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task SetOrderStock_PostsSalesForTheTarget()
    {
        var (a, b) = await SeedTwoBatchesAsync(10, 20);
        var order = TestData.Faker.Random.Guid();

        await SetAsync(order, (a, 4), (b, 5));

        (await QuantityAsync(a)).ShouldBe(6);
        (await QuantityAsync(b)).ShouldBe(15);
        var sales = await MovementsAsync(order);
        sales.ShouldAllBe(m => m.Type == StockMovementType.Sale && m.OrderId == order);
        sales
            .Select(m => (m.BatchId, m.Quantity, m.BalanceAfter))
            .ShouldBe([(a, -4, 6), (b, -5, 15)], ignoreOrder: true);
    }

    [Fact]
    public async Task SetOrderStock_SameTargetTwice_IsANoOp()
    {
        var (a, _) = await SeedTwoBatchesAsync(10, 20);
        var order = TestData.Faker.Random.Guid();

        await SetAsync(order, (a, 4));
        await SetAsync(order, (a, 4));

        (await QuantityAsync(a)).ShouldBe(6);
        (await MovementsAsync(order)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task SetOrderStock_ChangedOrder_MovesOnlyTheDifference()
    {
        var (a, b) = await SeedTwoBatchesAsync(10, 20);
        var order = TestData.Faker.Random.Guid();
        await SetAsync(order, (a, 4), (b, 5));

        // a: 4 -> 6 (sell 2 more), b: dropped from the order (5 back).
        await SetAsync(order, (a, 6));

        (await QuantityAsync(a)).ShouldBe(4);
        (await QuantityAsync(b)).ShouldBe(20);
        var latest = (await MovementsAsync(order)).Skip(2).ToList();
        latest
            .Select(m => (m.BatchId, m.Type, m.Quantity))
            .ShouldBe(
                [(a, StockMovementType.Sale, -2), (b, StockMovementType.SaleReversal, 5)],
                ignoreOrder: true
            );
    }

    [Fact]
    public async Task SetOrderStock_EmptyTarget_ReturnsEverythingAndRepeatIsANoOp()
    {
        var (a, b) = await SeedTwoBatchesAsync(10, 20);
        var order = TestData.Faker.Random.Guid();
        await SetAsync(order, (a, 4), (b, 5));

        await SetAsync(order);
        await SetAsync(order);

        (await QuantityAsync(a)).ShouldBe(10);
        (await QuantityAsync(b)).ShouldBe(20);
        (await MovementsAsync(order))
            .Count(m => m.Type == StockMovementType.SaleReversal)
            .ShouldBe(2);
    }

    [Fact]
    public async Task SetOrderStock_OrdersAreIndependent()
    {
        var (a, _) = await SeedTwoBatchesAsync(10, 20);
        var first = TestData.Faker.Random.Guid();
        var second = TestData.Faker.Random.Guid();

        await SetAsync(first, (a, 3));
        await SetAsync(second, (a, 4));
        await SetAsync(first);

        (await QuantityAsync(a)).ShouldBe(6);
    }

    [Fact]
    public async Task SetOrderStock_CanOversellIntoNegativeStock()
    {
        var (a, _) = await SeedTwoBatchesAsync(2, 20);

        await SetAsync(TestData.Faker.Random.Guid(), (a, 5));

        (await QuantityAsync(a)).ShouldBe(-3);
    }

    [Fact]
    public async Task SetOrderStock_UnknownBatch_IsSkippedAndReported()
    {
        var (a, _) = await SeedTwoBatchesAsync(10, 20);
        var unknown = TestData.Faker.Random.Guid();

        await using var context = _db.CreateContext();
        var skipped = await new StockRepo(context).SetOrderStockAsync(
            TestData.Faker.Random.Guid(),
            new Dictionary<Guid, int> { [a] = 1, [unknown] = 3 }
        );

        skipped.ShouldBe([unknown]);
        (await QuantityAsync(a)).ShouldBe(9);
    }

    [Fact]
    public async Task Ledger_QuantityAlwaysEqualsSumOfMovements()
    {
        var (a, b) = await SeedTwoBatchesAsync(10, 20);
        var order = TestData.Faker.Random.Guid();
        await SetAsync(order, (a, 4), (b, 5));
        await SetAsync(order, (a, 1));
        await using (var context = _db.CreateContext())
        {
            await new StockRepo(context).AdjustAsync(b, -2, AdjustmentReason.Damage, "Dropped");
        }

        await using var assertContext = _db.CreateContext();
        var sums = await assertContext
            .StockMovements.GroupBy(m => m.BatchId)
            .Select(g => new { BatchId = g.Key, Sum = g.Sum(m => m.Quantity) })
            .ToDictionaryAsync(x => x.BatchId, x => x.Sum, Ct);
        var quantities = await assertContext.ProductBatches.ToDictionaryAsync(
            x => x.Id,
            x => x.Quantity,
            Ct
        );
        sums[a].ShouldBe(quantities[a]!.Value);
        sums[b].ShouldBe(quantities[b]!.Value);
    }

    [Fact]
    public async Task SetOrderStock_MaintainsHasOrdersOnBatchesAndProduct()
    {
        var (a, b) = await SeedTwoBatchesAsync(10, 20);
        var order = TestData.Faker.Random.Guid();
        var other = TestData.Faker.Random.Guid();

        await SetAsync(order, (a, 3));
        (await FlagsAsync()).ShouldBe((true, false, true));

        // The order moves to the other batch.
        await SetAsync(order, (b, 2));
        (await FlagsAsync()).ShouldBe((false, true, true));

        // A second order on the same batch keeps it flagged after the first is released.
        await SetAsync(other, (b, 1));
        await SetAsync(order);
        (await FlagsAsync()).ShouldBe((false, true, true));

        await SetAsync(other);
        (await FlagsAsync()).ShouldBe((false, false, false));
        await AssertFlagsMatchLedgerAsync();

        async Task<(bool A, bool B, bool Product)> FlagsAsync()
        {
            await using var context = _db.CreateContext();
            var batches = await context
                .ProductBatches.AsNoTracking()
                .ToDictionaryAsync(x => x.Id, Ct);
            var product = await context.Products.AsNoTracking().SingleAsync(Ct);
            return (batches[a].HasOrders, batches[b].HasOrders, product.HasOrders);
        }
    }

    [Fact]
    public async Task HasOrders_StaysConsistentWithTheLedgerAcrossManyOrders()
    {
        var (a, b) = await SeedTwoBatchesAsync(100, 100);
        var orders = Enumerable.Range(0, 6).Select(_ => TestData.Faker.Random.Guid()).ToList();

        foreach (var order in orders)
        {
            var target = new List<(Guid, int)>();
            if (TestData.Faker.Random.Bool())
            {
                target.Add((a, TestData.Faker.Random.Int(1, 5)));
            }
            if (TestData.Faker.Random.Bool())
            {
                target.Add((b, TestData.Faker.Random.Int(1, 5)));
            }
            await SetAsync(order, [.. target]);
        }
        foreach (var order in orders.Where(_ => TestData.Faker.Random.Bool()))
        {
            await SetAsync(order);
        }

        await AssertFlagsMatchLedgerAsync();
    }

    // The stored flags must equal what the ledger implies.
    private async Task AssertFlagsMatchLedgerAsync()
    {
        await using var context = _db.CreateContext();
        var heldBatches = (
            await context
                .StockMovements.Where(m =>
                    m.OrderId != null
                    && (
                        m.Type == StockMovementType.Sale || m.Type == StockMovementType.SaleReversal
                    )
                )
                .GroupBy(m => new { m.BatchId, m.OrderId })
                .Where(g => g.Sum(m => m.Quantity) != 0)
                .Select(g => g.Key.BatchId)
                .ToListAsync(Ct)
        ).ToHashSet();
        var batches = await context.ProductBatches.AsNoTracking().ToListAsync(Ct);
        foreach (var batch in batches)
        {
            batch.HasOrders.ShouldBe(heldBatches.Contains(batch.Id), $"batch {batch.Lot}");
        }
        foreach (var product in await context.Products.AsNoTracking().ToListAsync(Ct))
        {
            product.HasOrders.ShouldBe(
                batches.Any(x => x.ProductId == product.Id && heldBatches.Contains(x.Id))
            );
        }
    }

    [Fact]
    public async Task AdjustAsync_RecordsReasonAndNote()
    {
        var (a, _) = await SeedTwoBatchesAsync(10, 20);
        var note = TestData.Faker.Lorem.Sentence();

        await using (var context = _db.CreateContext())
        {
            var movement = await new StockRepo(context).AdjustAsync(
                a,
                -3,
                AdjustmentReason.Expiry,
                note
            );
            movement.ShouldNotBeNull().BalanceAfter.ShouldBe(7);
            (
                await new StockRepo(context).AdjustAsync(
                    TestData.Faker.Random.Guid(),
                    1,
                    AdjustmentReason.Other,
                    null
                )
            ).ShouldBeNull();
        }

        (await QuantityAsync(a)).ShouldBe(7);
        await using var assertContext = _db.CreateContext();
        var saved = await assertContext.StockMovements.SingleAsync(
            m => m.Type == StockMovementType.Adjustment,
            Ct
        );
        saved.Reason.ShouldBe(AdjustmentReason.Expiry);
        saved.Note.ShouldBe(note);
        saved.CreatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetMovements_NewestFirstFilteredByDateAndScopedToBatchOrProduct()
    {
        var (a, b) = await SeedTwoBatchesAsync(10, 20);
        var old = TestData.Date(2020, 2020);
        var recent = TestData.Date(2024, 2024);
        await using (var context = _db.CreateContext())
        {
            var batches = await context.ProductBatches.ToDictionaryAsync(x => x.Id, Ct);
            batches[a].Move(StockMovementType.Receipt, 1).CreatedAt = old;
            batches[a].Move(StockMovementType.Receipt, 1).CreatedAt = recent;
            batches[b].Move(StockMovementType.Receipt, 1).CreatedAt = recent;
            await context.SaveChangesAsync(Ct);
        }
        var productId = (await FindBatchAsync(a)).ProductId!.Value;

        await using var readContext = _db.CreateContext();
        var repo = new StockRepo(readContext);

        var ofBatch = (await repo.GetBatchMovementsAsync(a, null, null)).ShouldNotBeNull().ToList();
        // The seed receipt (recorded now) comes first, then the back-dated ones, newest first.
        ofBatch.Count.ShouldBe(3);
        ofBatch.Skip(1).Select(m => m.CreatedAt).ShouldBe([recent, old]);
        ofBatch.ShouldAllBe(m => m.Batch!.Lot == "A");
        (await repo.GetBatchMovementsAsync(a, recent.AddDays(-1), recent.AddDays(1)))
            .ShouldNotBeNull()
            .ShouldHaveSingleItem()
            .CreatedAt.ShouldBe(recent);
        (await repo.GetBatchMovementsAsync(a, null, old.AddDays(1)))
            .ShouldNotBeNull()
            .ShouldHaveSingleItem()
            .CreatedAt.ShouldBe(old);
        (await repo.GetProductMovementsAsync(productId, null, null))
            .ShouldNotBeNull()
            .Count()
            .ShouldBe(5);
        (
            await repo.GetBatchMovementsAsync(TestData.Faker.Random.Guid(), null, null)
        ).ShouldBeNull();
        (
            await repo.GetProductMovementsAsync(TestData.Faker.Random.Guid(), null, null)
        ).ShouldBeNull();
    }

    [Fact]
    public async Task Holdings_AreTheNetSalesPerOrderAndBatch()
    {
        var (a, b) = await SeedTwoBatchesAsync(10, 20);
        var first = TestData.Faker.Random.Guid();
        var second = TestData.Faker.Random.Guid();
        var released = TestData.Faker.Random.Guid();
        await SetAsync(first, (a, 4), (b, 5));
        await SetAsync(first, (a, 3), (b, 5));
        await SetAsync(second, (a, 2));
        await SetAsync(released, (b, 7));
        await SetAsync(released);

        await using var context = _db.CreateContext();
        var repo = new StockRepo(context);

        (await repo.GetOrderHoldingsAsync(first)).ShouldBe(
            new Dictionary<Guid, int> { [a] = 3, [b] = 5 },
            ignoreOrder: true
        );
        (await repo.GetOrderHoldingsAsync(released)).ShouldBeEmpty();
        (await repo.GetBatchOrdersAsync(a))
            .ShouldNotBeNull()
            .ShouldBe([new(first, a, 3), new(second, a, 2)], ignoreOrder: true);
        var productId = (await FindBatchAsync(a)).ProductId!.Value;
        (await repo.GetProductOrdersAsync(productId))
            .ShouldNotBeNull()
            .ShouldBe([new(first, a, 3), new(first, b, 5), new(second, a, 2)], ignoreOrder: true);
    }

    [Fact]
    public async Task Holdings_UnknownBatchOrProduct_IsNull()
    {
        await using var context = _db.CreateContext();
        var repo = new StockRepo(context);

        (await repo.GetBatchOrdersAsync(TestData.Faker.Random.Guid())).ShouldBeNull();
        (await repo.GetProductOrdersAsync(TestData.Faker.Random.Guid())).ShouldBeNull();
    }

    // Stock enters through a receipt, as in the app, so the ledger starts balanced.
    private async Task<(Guid A, Guid B)> SeedTwoBatchesAsync(int quantityA, int quantityB)
    {
        var a = InventoryBuilder.Batch("A", 0);
        var b = InventoryBuilder.Batch("B", 0);
        a.Move(StockMovementType.Receipt, quantityA);
        b.Move(StockMovementType.Receipt, quantityB);
        await using var context = _db.CreateContext();
        context.Products.Add(InventoryBuilder.Product("P", a, b));
        await context.SaveChangesAsync(Ct);
        return (a.Id, b.Id);
    }

    private async Task SetAsync(Guid orderId, params (Guid BatchId, int Quantity)[] target)
    {
        await using var context = _db.CreateContext();
        await new StockRepo(context).SetOrderStockAsync(
            orderId,
            target.ToDictionary(t => t.BatchId, t => t.Quantity)
        );
    }

    private async Task<int?> QuantityAsync(Guid batchId) =>
        (await FindBatchAsync(batchId)).Quantity;

    private async Task<ProductBatch> FindBatchAsync(Guid batchId)
    {
        await using var context = _db.CreateContext();
        return await context.ProductBatches.AsNoTracking().SingleAsync(x => x.Id == batchId, Ct);
    }

    // In insertion order (CreatedAt, then the order they were added within one save).
    private async Task<List<StockMovement>> MovementsAsync(Guid orderId)
    {
        await using var context = _db.CreateContext();
        return await context
            .StockMovements.AsNoTracking()
            .Where(m => m.OrderId == orderId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(Ct);
    }
}
