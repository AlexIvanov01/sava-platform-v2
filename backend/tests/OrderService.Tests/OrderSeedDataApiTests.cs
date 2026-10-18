using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using OrderService.Dtos;
using Sava.Tests.Shared;

namespace OrderService.Tests;

// The gRPC substitutes return the Customer and Inventory seed data, so the startup sync makes
// every reference in the order seed file resolvable.
public abstract class SeedDataOrderApiFactory : OrderApiFactory
{
    protected SeedDataOrderApiFactory(MySqlFixture mySql)
        : base(mySql)
    {
        InventoryClient.ReturnAllItems().Returns(_ => SeedData.Items());
        CustomerClient.ReturnAllCustomers().Returns(_ => SeedData.Customers());
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

        var (expectedOrders, expectedInvoices) = SeedData.OrderCounts();
        var orders = page.ShouldNotBeNull().OrderFullReadDtos.ToList();
        orders.Count.ShouldBe(expectedOrders);
        orders.Count(o => o.Invoice != null).ShouldBe(expectedInvoices);
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
