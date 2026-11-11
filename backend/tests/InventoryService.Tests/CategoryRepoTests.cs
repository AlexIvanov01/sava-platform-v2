using InventoryService.DataAccess;
using InventoryService.Models;
using Microsoft.EntityFrameworkCore;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

[Trait("Category", "Integration")]
public class CategoryRepoTests(MySqlFixture mySql) : IAsyncLifetime
{
    private TestDatabase<ProductContext> _db = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        _db = await mySql.CreateDatabaseAsync(options => new ProductContext(options));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task GetAllCategoriesAsync_OrdersByNameWithProductCounts()
    {
        var a = InventoryBuilder.Category("A Dairy");
        var b = InventoryBuilder.Category("B Pantry");
        await SeedAsync(b, a);
        await SeedProductsAsync(a.Id, 2);

        await using var context = _db.CreateContext();
        var all = (await new CategoryRepo(context).GetAllCategoriesAsync()).ToList();

        all.Select(c => c.Category.Name).ShouldBe(["A Dairy", "B Pantry"]);
        all.Select(c => c.ProductCount).ShouldBe([2, 0]);
    }

    [Fact]
    public async Task GetCategoryAsync_MissingAndExisting()
    {
        var category = InventoryBuilder.Category();
        await SeedAsync(category);
        await SeedProductsAsync(category.Id, 1);

        await using var context = _db.CreateContext();
        var repo = new CategoryRepo(context);

        (await repo.GetCategoryAsync(TestData.Faker.Random.Guid())).ShouldBeNull();
        var found = (await repo.GetCategoryAsync(category.Id)).ShouldNotBeNull();
        found.Category.Name.ShouldBe(category.Name);
        found.ProductCount.ShouldBe(1);
        (await repo.CategoryExistsAsync(category.Id)).ShouldBeTrue();
    }

    [Fact]
    public async Task NameExistsAsync_IgnoresTheCategoryItself()
    {
        var category = InventoryBuilder.Category();
        await SeedAsync(category);

        await using var context = _db.CreateContext();
        var repo = new CategoryRepo(context);

        (await repo.NameExistsAsync(category.Name!)).ShouldBeTrue();
        (await repo.NameExistsAsync(category.Name!, category.Id)).ShouldBeFalse();
        (await repo.NameExistsAsync(InventoryBuilder.Category().Name!)).ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateCategoryAsync_OnlyOverwritesSuppliedFields()
    {
        var category = InventoryBuilder.Category();
        await SeedAsync(category);

        await using (var context = _db.CreateContext())
        {
            (
                await new CategoryRepo(context).UpdateCategoryAsync(
                    new Category { Id = category.Id, Name = "Renamed" }
                )
            )
                .ShouldNotBeNull()
                .Description.ShouldBe(category.Description);
            (
                await new CategoryRepo(context).UpdateCategoryAsync(
                    new Category { Id = TestData.Faker.Random.Guid() }
                )
            ).ShouldBeNull();
        }

        await using var assertContext = _db.CreateContext();
        var saved = await assertContext.Categories.SingleAsync(Ct);
        saved.Name.ShouldBe("Renamed");
        saved.Description.ShouldBe(category.Description);
        saved.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task DeleteCategoryAsync_RefusesWhileProductsReferenceIt()
    {
        var used = InventoryBuilder.Category();
        var unused = InventoryBuilder.Category();
        await SeedAsync(used, unused);
        await SeedProductsAsync(used.Id, 1);

        await using (var context = _db.CreateContext())
        {
            var repo = new CategoryRepo(context);
            (await repo.DeleteCategoryAsync(used.Id)).ShouldBe(DeleteCategoryStatus.HasProducts);
            (await repo.DeleteCategoryAsync(unused.Id)).ShouldBe(DeleteCategoryStatus.Deleted);
            (await repo.DeleteCategoryAsync(unused.Id)).ShouldBe(DeleteCategoryStatus.NotFound);
        }

        await using var assertContext = _db.CreateContext();
        (await assertContext.Categories.SingleAsync(Ct)).Id.ShouldBe(used.Id);
    }

    [Fact]
    public async Task CategoryName_IsUniqueInTheDatabase()
    {
        var category = InventoryBuilder.Category();
        await SeedAsync(category);

        await Should.ThrowAsync<DbUpdateException>(() =>
            SeedAsync(InventoryBuilder.Category(category.Name))
        );
    }

    private async Task SeedAsync(params Category[] categories)
    {
        await using var context = _db.CreateContext();
        context.Categories.AddRange(categories);
        await context.SaveChangesAsync(Ct);
    }

    private async Task SeedProductsAsync(Guid categoryId, int count)
    {
        await using var context = _db.CreateContext();
        for (var i = 0; i < count; i++)
        {
            var product = InventoryBuilder.Product();
            product.CategoryId = categoryId;
            context.Products.Add(product);
        }
        await context.SaveChangesAsync(Ct);
    }
}
