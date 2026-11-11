using InventoryService.DataAccess;
using InventoryService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

// The StockLedger migration records each existing batch's quantity as an opening balance.
[Trait("Category", "Integration")]
public class StockLedgerMigrationTests(MySqlFixture mySql)
{
    private const string PreviousMigration = "20261111214821_ProductCatalogFields";
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ExistingBatches_GetAnOpeningBalance()
    {
        var options = new DbContextOptionsBuilder<ProductContext>()
            .UseMySQL(mySql.GetConnectionString(MySqlFixture.NewDatabaseName("migration")))
            .Options;
        await using var context = new ProductContext(options);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration, Ct);

        var product = TestData.Faker.Random.Guid();
        var inStock = TestData.Faker.Random.Guid();
        var oversold = TestData.Faker.Random.Guid();
        var empty = TestData.Faker.Random.Guid();
        await context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO Products (Id, Name, SKU) VALUES ({product}, 'P', 'P-1');
            INSERT INTO ProductBatches (Id, ProductId, Quantity) VALUES
              ({inStock}, {product}, 12),
              ({oversold}, {product}, -2),
              ({empty}, {product}, 0);
            """,
            Ct
        );

        await migrator.MigrateAsync(cancellationToken: Ct);

        var movements = await context.StockMovements.AsNoTracking().ToListAsync(Ct);
        movements
            .Select(m => (m.BatchId, m.Quantity, m.BalanceAfter))
            .ShouldBe([(inStock, 12, 12), (oversold, -2, -2)], ignoreOrder: true);
        movements.ShouldAllBe(m =>
            m.Type == StockMovementType.OpeningBalance
            && m.CreatedBy == "system"
            && m.CreatedAt != null
        );
        movements.Select(m => m.Id).Distinct().Count().ShouldBe(2);
    }
}
