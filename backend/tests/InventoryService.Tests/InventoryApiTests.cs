using System.Net;
using System.Net.Http.Json;
using InventoryService.Dtos;

namespace InventoryService.Tests;

[Trait("Category", "Integration")]
public class InventoryApiTests(InventoryApiFactory factory) : IClassFixture<InventoryApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetProducts_ReturnsSeededDummyDataPage()
    {
        var page = await _client.GetFromJsonAsync<ProductPageReadDto>(
            "/api/products?pageSize=1",
            Ct
        );

        page.ShouldNotBeNull();
        page.ProductReadDtos.ShouldNotBeNull().Count().ShouldBe(1);
        page.Pages.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task GetProducts_PageSizeOutOfRange_Returns400(int pageSize)
    {
        var response = await _client.GetAsync($"/api/products?pageSize={pageSize}", Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ProductAndBatchLifecycle()
    {
        var create = await _client.PostAsJsonAsync(
            "/api/products",
            InventoryBuilder.ProductCreateDto("Lifecycle"),
            Ct
        );
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var product = (
            await create.Content.ReadFromJsonAsync<ProductReadDto>(Ct)
        ).ShouldNotBeNull();

        var addBatch = await _client.PostAsJsonAsync(
            $"/api/batches/{product.Id}",
            InventoryBuilder.BatchCreateDto("L1", 4),
            Ct
        );
        addBatch.StatusCode.ShouldBe(HttpStatusCode.Created);
        var batch = (await addBatch.Content.ReadFromJsonAsync<ProductReadDto>(Ct))
            .ShouldNotBeNull()
            .Batches.ShouldHaveSingleItem();

        var batchUpdate = await _client.PutAsJsonAsync(
            $"/api/batches/{batch.Id}",
            InventoryBuilder.BatchUpdateDto(quantity: 9),
            Ct
        );
        batchUpdate.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedBatch = (
            await batchUpdate.Content.ReadFromJsonAsync<BatchReadDto>(Ct)
        ).ShouldNotBeNull();
        updatedBatch.Quantity.ShouldBe(9);
        updatedBatch.Lot.ShouldBe("L1");
        updatedBatch.SellPrice.ShouldBe(batch.SellPrice);

        var productUpdate = await _client.PutAsJsonAsync(
            $"/api/products/{product.Id}",
            InventoryBuilder.ProductUpdateDto(name: "Renamed"),
            Ct
        );
        productUpdate.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updatedProduct = (
            await productUpdate.Content.ReadFromJsonAsync<ProductReadDto>(Ct)
        ).ShouldNotBeNull();
        updatedProduct.Name.ShouldBe("Renamed");
        updatedProduct.Brand.ShouldBe(product.Brand);

        var fetched = await _client.GetFromJsonAsync<ProductReadDto>(
            $"/api/products/{product.Id}",
            Ct
        );
        fetched.ShouldNotBeNull();
        fetched.Name.ShouldBe("Renamed");
        fetched.Brand.ShouldBe(product.Brand);
        fetched.Batches.ShouldHaveSingleItem().Quantity.ShouldBe(9);

        (await _client.DeleteAsync($"/api/batches/{batch.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );
        (await _client.DeleteAsync($"/api/products/{product.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );
        (await _client.GetAsync($"/api/products/{product.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );

        await factory
            .MessageBus.Received(1)
            .PublishNewItemAsync(
                Arg.Is<ItemPublishedDto>(p =>
                    p.Event == "Item_Published" && p.ExternalProductId == product.Id
                )
            );
        await factory
            .MessageBus.Received(1)
            .PublishNewItemAsync(
                Arg.Is<ItemPublishedDto>(p =>
                    p.Event == "Product_Deleted" && p.ExternalProductId == product.Id
                )
            );
    }

    [Fact]
    public async Task CreateProduct_MissingRequiredFields_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/products",
            new { name = "No brand" },
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateBatch_UnknownProduct_Returns404()
    {
        var response = await _client.PostAsJsonAsync(
            $"/api/batches/{Guid.NewGuid()}",
            InventoryBuilder.BatchCreateDto(),
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetProto_ReturnsProtoFile()
    {
        var proto = await _client.GetStringAsync("/protos/inventory.proto", Ct);

        proto.ShouldContain("service GrpcInventory");
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
