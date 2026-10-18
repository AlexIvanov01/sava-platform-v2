using System.Net;
using Microsoft.Extensions.Hosting;
using Sava.Tests.Shared;

namespace CustomerService.Tests;

public sealed class ProductionCustomerApiFactory(MySqlFixture mySql) : CustomerApiFactory(mySql)
{
    protected override string HostEnvironment => Environments.Production;
}

[Trait("Category", "Integration")]
public class CustomerSeedDataTests(ProductionCustomerApiFactory factory)
    : IClassFixture<ProductionCustomerApiFactory>
{
    [Fact]
    public async Task Production_MigratesButDoesNotSeedSampleData()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/api/customers",
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
