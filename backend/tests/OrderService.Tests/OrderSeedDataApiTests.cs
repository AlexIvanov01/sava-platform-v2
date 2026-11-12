using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using OrderService.Dtos;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

// The gRPC substitutes answer from the Customer and Inventory seed data, so every reference in
// the order seed file can be snapshotted at startup.
public abstract class SeedDataOrderApiFactory : OrderApiFactory
{
    protected SeedDataOrderApiFactory(MySqlFixture mySql)
        : base(mySql)
    {
        InventoryClient.Serves(SeedData.Catalog());
        foreach (var (id, customer) in SeedData.Customers())
        {
            CustomerClient.GetCustomerAsync(id).Returns(customer);
        }
    }
}

public sealed class DevelopmentSeedDataOrderApiFactory(MySqlFixture mySql)
    : SeedDataOrderApiFactory(mySql);

public sealed class ProductionSeedDataOrderApiFactory(MySqlFixture mySql)
    : SeedDataOrderApiFactory(mySql)
{
    protected override string HostEnvironment => Environments.Production;
}

[Trait("Category", "Integration")]
public class DevelopmentOrderSeedDataTests(DevelopmentSeedDataOrderApiFactory factory)
    : IClassFixture<DevelopmentSeedDataOrderApiFactory>
{
    [Fact]
    public async Task Development_SeedsOrdersAndInvoices()
    {
        using var client = factory.CreateClient();

        var page = await client.GetFromJsonAsync<OrderPageReadDto>(
            "/api/orders?pageSize=50",
            TestContext.Current.CancellationToken
        );

        var seeds = SeedData.Orders();
        var orders = page.ShouldNotBeNull().Orders.ToList();
        orders.Select(o => o.Id).ShouldBe(seeds.Select(s => s.Id), ignoreOrder: true);
        orders.ShouldAllBe(o => o.Customer != null && o.Items.Count > 0);
        foreach (var seed in seeds)
        {
            var order = orders.Single(o => o.Id == seed.Id);
            (order.Status, order.CompletedDate, order.CancelledDate).ShouldBe(
                (seed.Status, seed.CompletedDate, seed.CancelledDate),
                $"Order {seed.Id}"
            );
            (order.Invoice != null).ShouldBe(seed.Invoice != null, $"Order {seed.Id}");
        }
        orders.Where(o => o.Status == OrderStatus.Completed).ShouldAllBe(o => o.Invoice != null);
        orders
            .Where(o => o.Status is OrderStatus.InProcess or OrderStatus.Pending)
            .ShouldAllBe(o => o.Invoice == null);
        var invoiced = orders.Where(o => o.Invoice != null).ToList();
        invoiced.Count.ShouldBe(seeds.Count(s => s.Invoice != null));
        invoiced.ShouldAllBe(o => o.Invoice!.TotalAmount == o.Totals.Gross);
    }
}

[Trait("Category", "Integration")]
public class ProductionOrderSeedDataTests(ProductionSeedDataOrderApiFactory factory)
    : IClassFixture<ProductionSeedDataOrderApiFactory>
{
    [Fact]
    public async Task Production_DoesNotSeedOrders()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/orders", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
