using OrderService.DataAccess;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

// Gives each test class its own migrated database.
[Trait("Category", "Integration")]
public abstract class OrderDbTestBase(MySqlFixture mySql) : IAsyncLifetime
{
    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected TestDatabase<OrderContext> Db { get; private set; } = null!;

    public async ValueTask InitializeAsync() =>
        Db = await mySql.CreateDatabaseAsync(options => new OrderContext(options));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // Adds the entities and everything reachable from them in a single context.
    protected async Task SeedAsync(params object[] entities)
    {
        await using var context = Db.CreateContext();
        context.AddRange(entities);
        await context.SaveChangesAsync(Ct);
    }

    // An order with lineCount items of one batch each.
    protected async Task<Order> SeedOrderAsync(int lineCount = 2)
    {
        var order = OrderBuilder.Order([
            .. Enumerable.Range(0, lineCount).Select(_ => OrderBuilder.Item()),
        ]);
        await SeedAsync(order);
        return order;
    }
}
