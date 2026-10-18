using System.Net;
using Microsoft.Extensions.Hosting;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

public sealed class ProductionInventoryApiFactory(MySqlFixture mySql) : InventoryApiFactory(mySql)
{
    protected override string HostEnvironment => Environments.Production;
}

[Trait("Category", "Integration")]
public class InventorySeedDataTests(ProductionInventoryApiFactory factory)
    : IClassFixture<ProductionInventoryApiFactory>
{
    [Fact]
    public async Task Production_MigratesButDoesNotSeedSampleData()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/products",
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
