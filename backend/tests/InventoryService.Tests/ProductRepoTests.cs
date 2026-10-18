using InventoryService.DataAccess;
using InventoryService.Dtos;
using InventoryService.Mappings;
using InventoryService.Models;
using Microsoft.EntityFrameworkCore;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

[Trait("Category", "Integration")]
public class ProductRepoTests(MySqlFixture mySql) : IAsyncLifetime
{
    private TestDatabase<ProductContext> _db = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        _db = await mySql.CreateDatabaseAsync(options => new ProductContext(options));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Migrations_MatchTheModel()
    {
        await using var context = _db.CreateContext();

        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task CreateProductAsync_SetsDateAddedAndPersists()
    {
        var product = InventoryBuilder.ProductCreateDto("Created").ToEntity();

        await using (var context = _db.CreateContext())
        {
            await new ProductRepo(context).CreateProductAsync(product);
        }

        await using var assertContext = _db.CreateContext();
        var saved = await new ProductRepo(assertContext).GetProductAsync(product.Id);
        saved.ShouldNotBeNull();
        saved.Name.ShouldBe("Created");
        saved.ProductDateAdded.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetProductAsync_IncludesBatches()
    {
        var product = InventoryBuilder.Product(
            "P",
            InventoryBuilder.Batch("L1"),
            InventoryBuilder.Batch("L2")
        );
        await SeedAsync(product);

        await using var context = _db.CreateContext();
        var saved = await new ProductRepo(context).GetProductAsync(product.Id);

        saved
            .ShouldNotBeNull()
            .Batches.Select(b => b.Lot)
            .ShouldBe(["L1", "L2"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetAllProductsAsync_CursorPaging_VisitsEveryProductOnce()
    {
        var products = Enumerable
            .Range(1, 5)
            .Select(i => InventoryBuilder.Product($"P{i}"))
            .ToArray();
        await SeedAsync(products);
        var seen = new List<Guid>();
        Guid? cursor = null;

        await using var context = _db.CreateContext();
        var repo = new ProductRepo(context);
        while (true)
        {
            var page = (await repo.GetAllProductsAsync(cursor, 2)).ToList();
            if (page.Count == 0)
            {
                break;
            }

            page.Count.ShouldBeLessThanOrEqualTo(2);
            seen.AddRange(page.Select(p => p.Id));
            cursor = page[^1].Id;
        }

        seen.ShouldBe(products.Select(p => p.Id), ignoreOrder: true);
        (await repo.GetAllProductsCountAsync()).ShouldBe(5);
    }

    [Fact]
    public async Task UpdateProductAsync_OnlyOverwritesSuppliedFields()
    {
        var product = InventoryBuilder.Product("Old");
        await SeedAsync(product);

        await using (var context = _db.CreateContext())
        {
            var result = await new ProductRepo(context).UpdateProductAsync(
                InventoryBuilder.ProductUpdateDto(name: "New").ToEntity(product.Id)
            );
            result.ShouldNotBeNull();
            result.Name.ShouldBe("New");
            result.Brand.ShouldBe(product.Brand);
            result.ProductDateUpdated.ShouldNotBeNull();
        }

        await using var assertContext = _db.CreateContext();
        var saved = await assertContext.Products.SingleAsync(Ct);
        saved.Name.ShouldBe("New");
        saved.Brand.ShouldBe(product.Brand);
        saved.Barcode.ShouldBe(product.Barcode);
        saved.ProductDateUpdated.ShouldNotBeNull();
    }

    [Fact]
    public async Task UpdateProductAsync_Missing_ReturnsNull()
    {
        await using var context = _db.CreateContext();

        (
            await new ProductRepo(context).UpdateProductAsync(InventoryBuilder.Product())
        ).ShouldBeNull();
    }

    [Fact]
    public async Task DeleteProductAsync_CascadesToBatches()
    {
        var product = InventoryBuilder.Product("P", InventoryBuilder.Batch());
        await SeedAsync(product);

        await using (var context = _db.CreateContext())
        {
            (await new ProductRepo(context).DeleteProductAsync(product.Id)).ShouldNotBeNull();
            (await new ProductRepo(context).DeleteProductAsync(product.Id)).ShouldBeNull();
        }

        await using var assertContext = _db.CreateContext();
        (await assertContext.ProductBatches.AnyAsync(Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task AddBatchAsync_AddsBatchToExistingProductOnly()
    {
        var product = InventoryBuilder.Product("P");
        await SeedAsync(product);
        var batch = InventoryBuilder.BatchCreateDto("NEW").ToEntity();

        await using (var context = _db.CreateContext())
        {
            var result = await new ProductRepo(context).AddBatchAsync(batch, product.Id);
            result.ShouldNotBeNull().Batches.ShouldHaveSingleItem().Lot.ShouldBe("NEW");
        }

        await using var assertContext = _db.CreateContext();
        (await assertContext.Products.CountAsync(Ct)).ShouldBe(1);
        var saved = await assertContext.ProductBatches.SingleAsync(Ct);
        saved.ProductId.ShouldBe(product.Id);
        saved.BatchDateAdded.ShouldNotBeNull();
    }

    [Fact]
    public async Task AddBatchAsync_UnknownProduct_ReturnsNull()
    {
        await using var context = _db.CreateContext();

        (
            await new ProductRepo(context).AddBatchAsync(InventoryBuilder.Batch(), Guid.NewGuid())
        ).ShouldBeNull();
    }

    [Fact]
    public async Task UpdateBatchAsync_UpdatesSuppliedFieldsAndKeepsProduct()
    {
        var batch = InventoryBuilder.Batch("OLD", quantity: 10);
        var product = InventoryBuilder.Product("P", batch);
        await SeedAsync(product);

        await using (var context = _db.CreateContext())
        {
            var result = await new ProductRepo(context).UpdateBatchAsync(
                InventoryBuilder.BatchUpdateDto(lot: "NEW").ToEntity(batch.Id)
            );
            result.ShouldNotBeNull();
            result.Lot.ShouldBe("NEW");
            result.Quantity.ShouldBe(10);
            result.ProductId.ShouldBe(product.Id);
        }

        await using var assertContext = _db.CreateContext();
        (await assertContext.Products.CountAsync(Ct)).ShouldBe(1);
        var saved = await assertContext.ProductBatches.SingleAsync(Ct);
        saved.Lot.ShouldBe("NEW");
        saved.Quantity.ShouldBe(10);
        saved.SellPrice.ShouldBe(batch.SellPrice);
        saved.ProductId.ShouldBe(product.Id);
        saved.BatchDateUpdated.ShouldNotBeNull();
    }

    [Fact]
    public async Task UpdateBatchAsync_Missing_ReturnsNull()
    {
        await using var context = _db.CreateContext();

        (await new ProductRepo(context).UpdateBatchAsync(InventoryBuilder.Batch())).ShouldBeNull();
    }

    [Fact]
    public async Task DeleteBatchAsync_RemovesOnlyThatBatch()
    {
        var keep = InventoryBuilder.Batch("KEEP");
        var remove = InventoryBuilder.Batch("REMOVE");
        await SeedAsync(InventoryBuilder.Product("P", keep, remove));

        await using (var context = _db.CreateContext())
        {
            (await new ProductRepo(context).DeleteBatchAsync(remove.Id)).ShouldNotBeNull();
            (await new ProductRepo(context).DeleteBatchAsync(remove.Id)).ShouldBeNull();
        }

        await using var assertContext = _db.CreateContext();
        (await assertContext.ProductBatches.SingleAsync(Ct)).Id.ShouldBe(keep.Id);
    }

    [Fact]
    public async Task DecrementAndIncrementBatchesAsync_AdjustQuantitiesAndSumDuplicates()
    {
        var a = InventoryBuilder.Batch("A", quantity: 10);
        var b = InventoryBuilder.Batch("B", quantity: 20);
        await SeedAsync(InventoryBuilder.Product("P", a, b));
        OrderItemPublishedDto[] items =
        [
            new(a.Id, 2),
            new(a.Id, 3),
            new(b.Id, 5),
            new(Guid.NewGuid(), 99),
        ];

        await using (var context = _db.CreateContext())
        {
            await new ProductRepo(context).DecrementBatchesAsync(items);
        }

        var afterDecrement = await QuantitiesAsync();
        afterDecrement["A"].ShouldBe(5);
        afterDecrement["B"].ShouldBe(15);

        await using (var context = _db.CreateContext())
        {
            await new ProductRepo(context).IncrementBatchesAsync(items);
        }

        var afterIncrement = await QuantitiesAsync();
        afterIncrement["A"].ShouldBe(10);
        afterIncrement["B"].ShouldBe(20);
    }

    [Fact]
    public async Task GetAllProductBatchesAsync_ReturnsNamesAndBatches()
    {
        await SeedAsync(
            InventoryBuilder.Product("P", InventoryBuilder.Batch("L1")),
            InventoryBuilder.Product("Q")
        );

        await using var context = _db.CreateContext();
        var products = (await new ProductRepo(context).GetAllProductBatchesAsync()).ToList();

        products.Select(p => p.Name).ShouldBe(["P", "Q"], ignoreOrder: true);
        products.Single(p => p.Name == "P").Batches.ShouldHaveSingleItem().Lot.ShouldBe("L1");
    }

    private async Task<Dictionary<string, int?>> QuantitiesAsync()
    {
        await using var context = _db.CreateContext();
        return await context.ProductBatches.ToDictionaryAsync(b => b.Lot!, b => b.Quantity, Ct);
    }

    private async Task SeedAsync(params Product[] products)
    {
        await using var context = _db.CreateContext();
        context.Products.AddRange(products);
        await context.SaveChangesAsync(Ct);
    }
}
