using Microsoft.EntityFrameworkCore;
using OrderService.DataAccess;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class ItemRepoTests(MySqlFixture mySql) : OrderDbTestBase(mySql)
{
    [Fact]
    public async Task AddItemAsync_ThenExists()
    {
        var item = OrderBuilder.Item();

        await using var context = Db.CreateContext();
        var repo = new ItemRepo(context);
        (await repo.ExternalItemExistsAsync(item.ExternalBatchId)).ShouldBeFalse();
        await repo.AddItemAsync(item);
        (await repo.ExternalItemExistsAsync(item.ExternalBatchId)).ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateItemAsync_UpdatesSuppliedFields_KeepsProductId()
    {
        var item = OrderBuilder.Item("Old", 5m);
        await SeedAsync(item);

        await using (var context = Db.CreateContext())
        {
            await new ItemRepo(context).UpdateItemAsync(
                new Item
                {
                    ExternalBatchId = item.ExternalBatchId,
                    ExternalProductId = Guid.NewGuid(),
                    Name = "New",
                }
            );
            await new ItemRepo(context).UpdateItemAsync(
                new Item { ExternalBatchId = Guid.NewGuid(), Name = "Ghost" }
            );
        }

        await using var assert = Db.CreateContext();
        var saved = await assert.Items.SingleAsync(Ct);
        saved.Name.ShouldBe("New");
        saved.Price.ShouldBe(5m);
        saved.ExternalProductId.ShouldBe(item.ExternalProductId);
    }

    [Fact]
    public async Task UpdateProductNameAndDeleteByProduct_AffectAllBatchesOfThatProductOnly()
    {
        var productId = Guid.NewGuid();
        var a = OrderBuilder.Item("A");
        var b = OrderBuilder.Item("B");
        var other = OrderBuilder.Item("Other");
        a.ExternalProductId = b.ExternalProductId = productId;
        await SeedAsync(a, b, other);

        await using (var context = Db.CreateContext())
        {
            await new ItemRepo(context).UpdateProductNameAsync(productId, "Renamed");
        }
        await using (var assert = Db.CreateContext())
        {
            (
                await assert
                    .Items.Where(i => i.ExternalProductId == productId)
                    .Select(i => i.Name)
                    .ToListAsync(Ct)
            ).ShouldAllBe(n => n == "Renamed");
            (
                await assert.Items.SingleAsync(i => i.ExternalBatchId == other.ExternalBatchId, Ct)
            ).Name.ShouldBe("Other");
        }

        await using (var context = Db.CreateContext())
        {
            await new ItemRepo(context).DeleteItemsByProductIdAsync(productId);
            await new ItemRepo(context).DeleteItemAsync(other.ExternalBatchId);
        }
        await using var final = Db.CreateContext();
        (await final.Items.AnyAsync(Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task SyncItemsAsync_AddsUpdatesAndRemovesUnreferencedStaleItems()
    {
        var (_, ordered, _) = await SeedOrderAsync(1);
        var stale = OrderBuilder.Item("Stale");
        var existing = OrderBuilder.Item("Existing");
        await SeedAsync(stale, existing);
        var incoming = OrderBuilder.Item("Incoming");
        var updated = new Item
        {
            ExternalBatchId = existing.ExternalBatchId,
            ExternalProductId = existing.ExternalProductId,
            Name = "Existing v2",
            Price = 99m,
        };

        await using (var context = Db.CreateContext())
        {
            await new ItemRepo(context).SyncItemsAsync([incoming, updated]);
        }

        await using var assert = Db.CreateContext();
        var names = await assert.Items.ToDictionaryAsync(i => i.ExternalBatchId, i => i.Name, Ct);
        names.Keys.ShouldBe(
            [ordered[0].ExternalBatchId, existing.ExternalBatchId, incoming.ExternalBatchId],
            ignoreOrder: true
        );
        names[existing.ExternalBatchId].ShouldBe("Existing v2");
    }
}
