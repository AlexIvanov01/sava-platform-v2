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

    protected async Task<(Customer Customer, Item[] Items, Order Order)> SeedOrderAsync(
        int itemCount = 2
    )
    {
        var customer = OrderBuilder.Customer();
        var items = Enumerable
            .Range(1, itemCount)
            .Select(i => OrderBuilder.Item($"Item {i}"))
            .ToArray();
        var order = OrderBuilder.Order(customer, [.. items.Select((item, i) => (item, i + 1))]);
        await SeedAsync(customer, order);
        return (customer, items, order);
    }
}
