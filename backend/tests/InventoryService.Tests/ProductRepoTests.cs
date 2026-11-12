using InventoryService.Auditing;
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
        saved.CreatedAt.ShouldNotBeNull();
        saved.CreatedBy.ShouldBe(HttpContextCurrentUser.SystemName);
        saved.UpdatedAt.ShouldBeNull();
        saved.UpdatedBy.ShouldBeNull();
    }

    [Fact]
    public async Task SaveChanges_StampsCurrentUserOnCreateAndUpdate()
    {
        var creator = TestData.Faker.Internet.UserName();
        var editor = TestData.Faker.Internet.UserName();
        var product = InventoryBuilder.Product("Audited", InventoryBuilder.Batch("L1"));

        await using (var context = CreateContextAs(creator))
        {
            await new ProductRepo(context).CreateProductAsync(product);
        }

        await using (var context = CreateContextAs(editor))
        {
            await new ProductRepo(context).UpdateBatchAsync(
                InventoryBuilder.BatchUpdateDto(quantity: 1).ToEntity(product.Batches[0].Id)
            );
        }

        await using var assertContext = _db.CreateContext();
        var saved = await new ProductRepo(assertContext).GetProductAsync(product.Id);
        saved.ShouldNotBeNull();
        saved.CreatedBy.ShouldBe(creator);
        saved.UpdatedBy.ShouldBeNull();
        var batch = saved.Batches.ShouldHaveSingleItem();
        batch.CreatedBy.ShouldBe(creator);
        batch.UpdatedBy.ShouldBe(editor);
        batch.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task SaveChanges_KeepsCreatedAtSuppliedBySeedData()
    {
        var seededDate = TestData.Date(2020, 2024);
        var product = InventoryBuilder.Product("Seeded");
        product.CreatedAt = seededDate;
        await SeedAsync(product);

        await using var context = _db.CreateContext();
        var saved = await context.Products.SingleAsync(Ct);

        saved.CreatedAt.ShouldBe(seededDate);
    }

    private ProductContext CreateContextAs(string userName)
    {
        var user = Substitute.For<ICurrentUser>();
        user.Name.Returns(userName);
        return new ProductContext(_db.Options, user);
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
            var page = (await repo.GetAllProductsAsync(ProductFilter.None, cursor, 2)).ToList();
            if (page.Count == 0)
            {
                break;
            }

            page.Count.ShouldBeLessThanOrEqualTo(2);
            seen.AddRange(page.Select(p => p.Id));
            cursor = page[^1].Id;
        }

        seen.ShouldBe(products.Select(p => p.Id), ignoreOrder: true);
        (await repo.GetAllProductsCountAsync(ProductFilter.None)).ShouldBe(5);
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
            result.UpdatedAt.ShouldNotBeNull();
        }

        await using var assertContext = _db.CreateContext();
        var saved = await assertContext.Products.SingleAsync(Ct);
        saved.Name.ShouldBe("New");
        saved.Brand.ShouldBe(product.Brand);
        saved.Barcode.ShouldBe(product.Barcode);
        saved.UpdatedAt.ShouldNotBeNull();
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
            (await new ProductRepo(context).DeleteProductAsync(product.Id)).Status.ShouldBe(
                DeleteStatus.Deleted
            );
            (await new ProductRepo(context).DeleteProductAsync(product.Id)).Status.ShouldBe(
                DeleteStatus.NotFound
            );
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
        saved.CreatedAt.ShouldNotBeNull();
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
        saved.UpdatedAt.ShouldNotBeNull();
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
            (await new ProductRepo(context).DeleteBatchAsync(remove.Id)).Status.ShouldBe(
                DeleteStatus.Deleted
            );
            (await new ProductRepo(context).DeleteBatchAsync(remove.Id)).Status.ShouldBe(
                DeleteStatus.NotFound
            );
        }

        await using var assertContext = _db.CreateContext();
        (await assertContext.ProductBatches.SingleAsync(Ct)).Id.ShouldBe(keep.Id);
    }

    [Fact]
    public async Task AddBatchAsync_RecordsTheInitialQuantityAsAReceipt()
    {
        var product = InventoryBuilder.Product("P");
        await SeedAsync(product);
        var batch = InventoryBuilder.BatchCreateDto("NEW", quantity: 25).ToEntity();

        await using (var context = _db.CreateContext())
        {
            await new ProductRepo(context).AddBatchAsync(batch, product.Id);
        }

        await using var assertContext = _db.CreateContext();
        var saved = await assertContext.ProductBatches.SingleAsync(Ct);
        saved.Quantity.ShouldBe(25);
        var receipt = await assertContext.StockMovements.SingleAsync(Ct);
        receipt.BatchId.ShouldBe(saved.Id);
        receipt.Type.ShouldBe(StockMovementType.Receipt);
        receipt.Quantity.ShouldBe(25);
        receipt.BalanceAfter.ShouldBe(25);
        receipt.Reason.ShouldBeNull();
        receipt.OrderId.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateBatchAsync_QuantityEdit_IsRecordedAsCountCorrection()
    {
        var batch = InventoryBuilder.Batch("B", quantity: 10);
        await SeedAsync(InventoryBuilder.Product("P", batch));

        await using (var context = _db.CreateContext())
        {
            await new ProductRepo(context).UpdateBatchAsync(
                InventoryBuilder.BatchUpdateDto(quantity: 7).ToEntity(batch.Id)
            );
            // Same quantity again, and a field-only edit: no movement.
            await new ProductRepo(context).UpdateBatchAsync(
                InventoryBuilder.BatchUpdateDto(quantity: 7, lot: "B2").ToEntity(batch.Id)
            );
        }

        await using var assertContext = _db.CreateContext();
        (await assertContext.ProductBatches.SingleAsync(Ct)).Quantity.ShouldBe(7);
        var correction = await assertContext.StockMovements.SingleAsync(Ct);
        correction.Type.ShouldBe(StockMovementType.Adjustment);
        correction.Reason.ShouldBe(AdjustmentReason.CountCorrection);
        correction.Quantity.ShouldBe(-3);
        correction.BalanceAfter.ShouldBe(7);
    }

    [Fact]
    public async Task Delete_ProductOrBatchWithOrders_IsRefused()
    {
        var batch = InventoryBuilder.Batch();
        batch.HasOrders = true;
        var product = InventoryBuilder.Product("P", batch);
        product.HasOrders = true;
        await SeedAsync(product);

        await using (var context = _db.CreateContext())
        {
            var repo = new ProductRepo(context);
            (await repo.DeleteBatchAsync(batch.Id)).Status.ShouldBe(DeleteStatus.HasOrders);
            (await repo.DeleteProductAsync(product.Id)).Status.ShouldBe(DeleteStatus.HasOrders);
        }

        await using var assertContext = _db.CreateContext();
        (await assertContext.ProductBatches.CountAsync(Ct)).ShouldBe(1);
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

    [Fact]
    public async Task GetAllProducts_FiltersCombineAndCountMatches()
    {
        var category = InventoryBuilder.Category();
        await SeedCategoriesAsync(category);
        var match = InventoryBuilder.Product("Kashkaval Premium");
        match.CategoryId = category.Id;
        match.IsPublished = true;
        var sameNameOtherCategory = InventoryBuilder.Product("Kashkaval Light");
        sameNameOtherCategory.IsPublished = true;
        var sameCategoryUnpublished = InventoryBuilder.Product("Kashkaval Old");
        sameCategoryUnpublished.CategoryId = category.Id;
        sameCategoryUnpublished.IsPublished = false;
        var unrelated = InventoryBuilder.Product("Yogurt");
        await SeedAsync(match, sameNameOtherCategory, sameCategoryUnpublished, unrelated);
        var filter = new ProductFilter(
            Name: "kashkaval",
            CategoryId: category.Id,
            IsPublished: true
        );

        await using var context = _db.CreateContext();
        var repo = new ProductRepo(context);
        var page = (await repo.GetAllProductsAsync(filter, null, 10)).ToList();

        page.ShouldHaveSingleItem().Id.ShouldBe(match.Id);
        page[0].Category.ShouldNotBeNull().Name.ShouldBe(category.Name);
        (await repo.GetAllProductsCountAsync(filter)).ShouldBe(1);
        (await repo.GetAllProductsCountAsync(new ProductFilter(Name: "kashkaval"))).ShouldBe(3);
    }

    [Fact]
    public async Task GetAllProducts_SkuMatchesSubstringAndBarcodeMatchesExactly()
    {
        var product = InventoryBuilder.Product();
        await SeedAsync(product, InventoryBuilder.Product());

        await using var context = _db.CreateContext();
        var repo = new ProductRepo(context);

        (await repo.GetAllProductsAsync(new ProductFilter(SKU: product.SKU![4..12]), null, 10))
            .ShouldHaveSingleItem()
            .Id.ShouldBe(product.Id);
        (await repo.GetAllProductsAsync(new ProductFilter(Barcode: product.Barcode), null, 10))
            .ShouldHaveSingleItem()
            .Id.ShouldBe(product.Id);
        (
            await repo.GetAllProductsAsync(
                new ProductFilter(Barcode: product.Barcode![..6]),
                null,
                10
            )
        ).ShouldBeEmpty();
    }

    [Fact]
    public async Task SkuExistsAsync_IgnoresTheProductItself()
    {
        var product = InventoryBuilder.Product();
        await SeedAsync(product);

        await using var context = _db.CreateContext();
        var repo = new ProductRepo(context);

        (await repo.SkuExistsAsync(product.SKU!)).ShouldBeTrue();
        (await repo.SkuExistsAsync(product.SKU!, product.Id)).ShouldBeFalse();
        (await repo.SkuExistsAsync(product.SKU!, TestData.Faker.Random.Guid())).ShouldBeTrue();
        (await repo.SkuExistsAsync(InventoryBuilder.Sku())).ShouldBeFalse();
    }

    [Fact]
    public async Task Sku_IsUniqueInTheDatabase()
    {
        var product = InventoryBuilder.Product();
        await SeedAsync(product);
        var duplicate = InventoryBuilder.Product();
        duplicate.SKU = product.SKU;

        await Should.ThrowAsync<DbUpdateException>(() => SeedAsync(duplicate));
    }

    [Fact]
    public async Task UpdateProductAsync_UpdatesCatalogFieldsAndLoadsCategory()
    {
        var category = InventoryBuilder.Category();
        await SeedCategoriesAsync(category);
        var product = InventoryBuilder.Product();
        product.IsPublished = false;
        await SeedAsync(product);
        var sku = InventoryBuilder.Sku();
        var listPrice = TestData.Faker.Finance.Amount(1, 100);

        await using (var context = _db.CreateContext())
        {
            var result = await new ProductRepo(context).UpdateProductAsync(
                InventoryBuilder
                    .ProductUpdateDto(
                        sku: sku,
                        listPrice: listPrice,
                        categoryId: category.Id,
                        isPublished: true
                    )
                    .ToEntity(product.Id)
            );
            result.ShouldNotBeNull().Category.ShouldNotBeNull().Name.ShouldBe(category.Name);
        }

        await using var assertContext = _db.CreateContext();
        var saved = await assertContext.Products.SingleAsync(Ct);
        saved.SKU.ShouldBe(sku);
        saved.ListPrice.ShouldBe(listPrice);
        saved.CategoryId.ShouldBe(category.Id);
        saved.IsPublished.ShouldBe(true);
        saved.VatRate.ShouldBe(product.VatRate);
        saved.UnitOfMeasure.ShouldBe(product.UnitOfMeasure);
    }

    private async Task SeedCategoriesAsync(params Category[] categories)
    {
        await using var context = _db.CreateContext();
        context.Categories.AddRange(categories);
        await context.SaveChangesAsync(Ct);
    }

    private async Task SeedAsync(params Product[] products)
    {
        await using var context = _db.CreateContext();
        context.Products.AddRange(products);
        await context.SaveChangesAsync(Ct);
    }
}
