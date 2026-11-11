using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CustomerService.Dtos;
using Sava.Tests.Shared;

namespace CustomerService.Tests;

[Trait("Category", "Integration")]
public class CustomerApiTests(CustomerApiFactory factory) : IClassFixture<CustomerApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetCustomers_ReturnsSeededDummyData()
    {
        var customers = await _client.GetFromJsonAsync<List<CustomerReadDto>>("/api/customers", Ct);

        customers.ShouldNotBeNull().Count.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public async Task CustomerLifecycle_CreateGetUpdateDelete()
    {
        var creator = TestData.Faker.Internet.UserName();
        var editor = TestData.Faker.Internet.UserName();
        _client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(
            TestTokens.Bearer(creator)
        );

        var create = await _client.PostAsJsonAsync(
            "/api/customers",
            CustomerBuilder.CreateDto("Lifecycle"),
            Ct
        );
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = (
            await create.Content.ReadFromJsonAsync<CustomerReadDto>(Ct)
        ).ShouldNotBeNull();
        create
            .Headers.Location.ShouldNotBeNull()
            .ToString()
            .ShouldEndWith($"/api/customers/{created.Id}", Case.Insensitive);
        created.Audit.CreatedBy.ShouldBe(creator);
        created.Audit.CreatedAt.ShouldNotBeNull();
        created.Audit.UpdatedBy.ShouldBeNull();

        _client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(
            TestTokens.Bearer(editor)
        );
        var update = await _client.PutAsJsonAsync(
            $"/api/customers/{created.Id}",
            CustomerBuilder.UpdateDto(city: "Varna"),
            Ct
        );
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = (
            await update.Content.ReadFromJsonAsync<CustomerReadDto>(Ct)
        ).ShouldNotBeNull();
        updated.City.ShouldBe("Varna");
        updated.Name.ShouldBe("Lifecycle");
        updated.Email.ShouldBe(created.Email);
        updated.Audit.CreatedBy.ShouldBe(creator);
        updated.Audit.UpdatedBy.ShouldBe(editor);
        updated.Audit.UpdatedAt.ShouldNotBeNull();

        _client.DefaultRequestHeaders.Authorization = null;
        var fetched = await _client.GetFromJsonAsync<CustomerReadDto>(
            $"/api/customers/{created.Id}",
            Ct
        );
        fetched.ShouldNotBeNull();
        fetched.City.ShouldBe("Varna");
        fetched.Name.ShouldBe("Lifecycle");
        fetched.Audit.UpdatedBy.ShouldBe(editor);

        (await _client.DeleteAsync($"/api/customers/{created.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );
        (await _client.GetAsync($"/api/customers/{created.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );

        await factory
            .MessageBus.Received(1)
            .PublishNewCustomerAsync(
                Arg.Is<CustomerPublishedDto>(p =>
                    p.Event == "Customer_Deleted" && p.ExternalId == created.Id
                )
            );
    }

    [Fact]
    public async Task CreateCustomer_MissingName_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/customers",
            new { companyName = "No name" },
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task UnknownCustomer_Returns404(string method)
    {
        var request = new HttpRequestMessage(
            new HttpMethod(method),
            $"/api/customers/{Guid.NewGuid()}"
        )
        {
            Content = method == "PUT" ? JsonContent.Create(CustomerBuilder.UpdateDto("x")) : null,
        };

        (await _client.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetProto_ReturnsProtoFile()
    {
        var proto = await _client.GetStringAsync("/protos/customer.proto", Ct);

        proto.ShouldContain("service GrpcCustomer");
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/readyz")]
    public async Task HealthEndpoints_Return200WhenDatabaseIsReachable(string path)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(
            "Healthy"
        );
    }
}
