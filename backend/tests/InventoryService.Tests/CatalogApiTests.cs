using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InventoryService.Dtos;
using InventoryService.Models;
using Microsoft.AspNetCore.Mvc;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

[Trait("Category", "Integration")]
public class CatalogApiTests(InventoryApiFactory factory) : IClassFixture<InventoryApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DevelopmentSeed_HasCategoriesAndCatalogFields()
    {
        var categories = await _client.GetFromJsonAsync<List<CategoryReadDto>>(
            "/api/categories",
            Ct
        );
        var dairy = categories.ShouldNotBeNull().Single(c => c.Name == "Dairy");
        dairy.ProductCount.ShouldBe(3);

        var page = await _client.GetFromJsonAsync<ProductPageReadDto>(
            $"/api/products?categoryId={dairy.Id}&pageSize=100",
            Ct
        );

        var products = page.ShouldNotBeNull().ProductReadDtos.ShouldNotBeNull().ToList();
        products.Count.ShouldBe(3);
        products.ShouldAllBe(p => p.CategoryName == "Dairy" && p.SKU!.StartsWith("DAI-"));
        products.ShouldAllBe(p => p.ListPrice > 0 && p.VatRate == 20m);
        products.SelectMany(p => p.Batches).ShouldAllBe(b => b.Location == "Cold room 1");
    }

    [Fact]
    public async Task GetProducts_FiltersBySkuBarcodeAndPublished()
    {
        var bySku = await _client.GetFromJsonAsync<ProductPageReadDto>(
            "/api/products?sku=ALC-&pageSize=100",
            Ct
        );
        bySku
            .ShouldNotBeNull()
            .ProductReadDtos.ShouldNotBeNull()
            .Select(p => p.SKU)
            .ShouldBe(["ALC-001", "ALC-002"], ignoreOrder: true);
        bySku.Pages.ShouldBe(1);

        var byBarcode = await _client.GetFromJsonAsync<ProductPageReadDto>(
            "/api/products?barcode=3801001000375",
            Ct
        );
        byBarcode
            .ShouldNotBeNull()
            .ProductReadDtos.ShouldNotBeNull()
            .ShouldHaveSingleItem()
            .SKU.ShouldBe("DAI-001");

        var unpublished = await _client.GetFromJsonAsync<ProductPageReadDto>(
            "/api/products?isPublished=false&pageSize=100",
            Ct
        );
        unpublished
            .ShouldNotBeNull()
            .ProductReadDtos.ShouldNotBeNull()
            .ShouldAllBe(p => !p.IsPublished);

        (await _client.GetAsync("/api/products?name=no-such-product", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );
    }

    [Fact]
    public async Task UnitOfMeasure_IsSerializedByName()
    {
        var page = await _client.GetStringAsync("/api/products?sku=BEV-001", Ct);

        using var json = JsonDocument.Parse(page);
        json.RootElement.GetProperty("productReadDtos")[0]
            .GetProperty("unitOfMeasure")
            .GetString()
            .ShouldBe(nameof(UnitOfMeasure.Pack));
    }

    [Fact]
    public async Task CategoryLifecycle_BlocksDeleteWhileProductsUseIt()
    {
        var createDto = new CategoryCreateDto(
            InventoryBuilder.Category().Name!,
            TestData.Faker.Lorem.Sentence()
        );
        var create = await _client.PostAsJsonAsync("/api/categories", createDto, Ct);
        create.StatusCode.ShouldBe(HttpStatusCode.Created);
        var category = (
            await create.Content.ReadFromJsonAsync<CategoryReadDto>(Ct)
        ).ShouldNotBeNull();
        category.Name.ShouldBe(createDto.Name);
        category.ProductCount.ShouldBe(0);
        create
            .Headers.Location.ShouldNotBeNull()
            .ToString()
            .ShouldEndWith($"/api/categories/{category.Id}", Case.Insensitive);

        (await _client.PostAsJsonAsync("/api/categories", createDto, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.Conflict
        );

        var productDto = InventoryBuilder.ProductCreateDto(categoryId: category.Id);
        var productResponse = await _client.PostAsJsonAsync("/api/products", productDto, Ct);
        productResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        var product = (
            await productResponse.Content.ReadFromJsonAsync<ProductReadDto>(Ct)
        ).ShouldNotBeNull();
        product.CategoryId.ShouldBe(category.Id);
        product.CategoryName.ShouldBe(category.Name);
        product.SKU.ShouldBe(productDto.SKU);
        product.ListPrice.ShouldBe(productDto.ListPrice);
        product.VatRate.ShouldBe(productDto.VatRate!.Value);
        product.UnitOfMeasure.ShouldBe(productDto.UnitOfMeasure!.Value);
        product.IsPublished.ShouldBe(productDto.IsPublished!.Value);

        var rename = await _client.PutAsJsonAsync(
            $"/api/categories/{category.Id}",
            new CategoryUpdateDto("Renamed " + category.Name![..20], null),
            Ct
        );
        rename.StatusCode.ShouldBe(HttpStatusCode.OK);
        var renamed = (
            await rename.Content.ReadFromJsonAsync<CategoryReadDto>(Ct)
        ).ShouldNotBeNull();
        renamed.ProductCount.ShouldBe(1);
        renamed.Description.ShouldBe(createDto.Description);
        (await _client.GetFromJsonAsync<ProductReadDto>($"/api/products/{product.Id}", Ct))
            .ShouldNotBeNull()
            .CategoryName.ShouldBe(renamed.Name);

        var blocked = await _client.DeleteAsync($"/api/categories/{category.Id}", Ct);
        blocked.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blocked.Content.ReadFromJsonAsync<ProblemDetails>(Ct))
            .ShouldNotBeNull()
            .Title.ShouldBe("Category has products");

        (await _client.DeleteAsync($"/api/products/{product.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );
        (await _client.DeleteAsync($"/api/categories/{category.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );
        (await _client.GetAsync($"/api/categories/{category.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );
    }

    [Fact]
    public async Task Product_ExposesCostStockAndEbitDerivedFromBatches()
    {
        var create = InventoryBuilder.ProductCreateDto() with { ListPrice = 10.00m };
        var created = await _client.PostAsJsonAsync("/api/products", create, Ct);
        var product = (
            await created.Content.ReadFromJsonAsync<ProductReadDto>(Ct)
        ).ShouldNotBeNull();
        product.Cost.ShouldBeNull();
        product.AvailableQuantity.ShouldBe(0);
        product.Ebit.ShouldBeNull();

        foreach (var (quantity, purchasePrice) in new[] { (12, 6.00m), (-2, 9.00m), (5, 7.50m) })
        {
            var batch = InventoryBuilder.BatchCreateDto(quantity: quantity) with
            {
                PurchasePrice = purchasePrice,
                ExpirationDate = TestData.Date(2090, 2099),
            };
            (
                await _client.PostAsJsonAsync($"/api/batches/{product.Id}", batch, Ct)
            ).StatusCode.ShouldBe(HttpStatusCode.Created);
        }

        var fetched = (
            await _client.GetFromJsonAsync<ProductReadDto>($"/api/products/{product.Id}", Ct)
        ).ShouldNotBeNull();
        fetched.Cost.ShouldBe(7.50m);
        fetched.AvailableQuantity.ShouldBe(15);
        fetched.Ebit.ShouldBe(2.50m);
        fetched.EbitPercent.ShouldBe(25.00m);

        // PUT returns the saved product with its batches, so the figures are recomputed there too.
        var update = await _client.PutAsJsonAsync(
            $"/api/products/{product.Id}",
            InventoryBuilder.ProductUpdateDto(listPrice: 15.00m),
            Ct
        );
        var updated = (
            await update.Content.ReadFromJsonAsync<ProductReadDto>(Ct)
        ).ShouldNotBeNull();
        updated.Batches.Count.ShouldBe(3);
        updated.Cost.ShouldBe(7.50m);
        updated.Ebit.ShouldBe(7.50m);
        updated.EbitPercent.ShouldBe(50.00m);
    }

    [Fact]
    public async Task CreateProduct_DuplicateSku_Returns409()
    {
        var first = InventoryBuilder.ProductCreateDto();
        (await _client.PostAsJsonAsync("/api/products", first, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.Created
        );

        var duplicate = InventoryBuilder.ProductCreateDto() with { SKU = first.SKU };
        var response = await _client.PostAsJsonAsync("/api/products", duplicate, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct))
            .ShouldNotBeNull()
            .Title.ShouldBe("SKU already exists");
    }

    [Fact]
    public async Task CreateProduct_UnknownCategory_Returns400()
    {
        var dto = InventoryBuilder.ProductCreateDto(categoryId: TestData.Faker.Random.Guid());

        var response = await _client.PostAsJsonAsync("/api/products", dto, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    public static TheoryData<string, object?> InvalidProductFields =>
        new()
        {
            { "sku", null },
            { "listPrice", null },
            { "listPrice", -1 },
            { "vatRate", 150 },
            { "unitOfMeasure", "Barrel" },
        };

    [Theory]
    [MemberData(nameof(InvalidProductFields))]
    public async Task CreateProduct_InvalidCatalogField_Returns400(string field, object? value)
    {
        var json = JsonSerializer.SerializeToNode(
            InventoryBuilder.ProductCreateDto(),
            JsonSerializerOptions.Web
        )!;
        json[field] = value == null ? null : JsonSerializer.SerializeToNode(value);

        var response = await _client.PostAsJsonAsync("/api/products", json, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
