using InventoryService.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

// The ProductCatalogFields migration backfills existing rows with hand-written SQL; run it on
// data created under the previous schema.
[Trait("Category", "Integration")]
public class ProductCatalogMigrationTests(MySqlFixture mySql)
{
    private const string PreviousMigration = "20261111205144_AuditFields";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExistingProducts_GetSkuListPriceVatAndUnit()
    {
        var options = new DbContextOptionsBuilder<ProductContext>()
            .UseMySQL(mySql.GetConnectionString(MySqlFixture.NewDatabaseName("migration")))
            .Options;
        await using var context = new ProductContext(options);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, Ct);

        var older = TestData.Faker.Random.Guid();
        var newer = TestData.Faker.Random.Guid();
        var withoutBatches = TestData.Faker.Random.Guid();
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO Products (Id, Name, CreatedAt) VALUES
              ({older}, 'Older', '2024-01-01'),
              ({newer}, 'Newer', '2025-01-01'),
              ({withoutBatches}, 'No batches', '2026-01-01');
            INSERT INTO ProductBatches (Id, ProductId, SellPrice) VALUES
              ({TestData.Faker.Random.Guid()}, {older}, 4.50),
              ({TestData.Faker.Random.Guid()}, {older}, 5.25),
              ({TestData.Faker.Random.Guid()}, {newer}, 1.99);
            """,
            Ct
        );

        await migrator.MigrateAsync(cancellationToken: Ct);

        var products = await context
            .Products.AsNoTracking()
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(Ct);
        products.Select(p => p.SKU).ShouldBe(["P-000001", "P-000002", "P-000003"]);
        products.Select(p => p.ListPrice).ShouldBe([5.25m, 1.99m, null]);
        products.ShouldAllBe(p => p.VatRate == 20m);
        products.ShouldAllBe(p => p.UnitOfMeasure == Models.UnitOfMeasure.Piece);
        products.ShouldAllBe(p => p.IsPublished == false);
        (await context.Database.GetPendingMigrationsAsync(Ct)).ShouldBeEmpty();
    }
}
