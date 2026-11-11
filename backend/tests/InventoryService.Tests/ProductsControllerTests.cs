using InventoryService.AsyncDataServices;
using InventoryService.Controllers;
using InventoryService.DataAccess;
using InventoryService.Dtos;
using InventoryService.Models;
using Microsoft.AspNetCore.Mvc;
using NSubstitute.ExceptionExtensions;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

public class ProductsControllerTests
{
    private readonly IProductRepo _repo = Substitute.For<IProductRepo>();
    private readonly ICategoryRepo _categories = Substitute.For<ICategoryRepo>();
    private readonly IMessageBusClient _bus = Substitute.For<IMessageBusClient>();
    private readonly ProductsController _controller;

    public ProductsControllerTests() =>
        _controller = new ProductsController(_repo, _categories, _bus);

    [Fact]
    public async Task GetProducts_Empty_ReturnsNotFound()
    {
        _repo.GetAllProductsAsync(ProductFilter.None, null, 10).Returns(new List<Product>());

        var result = await _controller.GetProducts(null);

        result.Result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetProducts_ReturnsPageWithLastIdAsCursorAndPageCount()
    {
        var cursor = Guid.NewGuid();
        var products = new List<Product>
        {
            InventoryBuilder.Product("A"),
            InventoryBuilder.Product("B"),
        };
        _repo.GetAllProductsAsync(ProductFilter.None, cursor, 2).Returns(products);
        _repo.GetAllProductsCountAsync(ProductFilter.None).Returns(5);

        var result = await _controller.GetProducts(cursor, 2);

        var page = result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<ProductPageReadDto>();
        page.ProductReadDtos.ShouldNotBeNull().Select(p => p.Name).ShouldBe(["A", "B"]);
        page.Cursor.ShouldBe(products[1].Id);
        page.Pages.ShouldBe(3);
    }

    [Fact]
    public async Task GetProducts_RepoThrows_Returns500()
    {
        _repo
            .GetAllProductsAsync(Arg.Any<ProductFilter>(), Arg.Any<Guid?>(), Arg.Any<int>())
            .ThrowsAsync(new InvalidOperationException());

        var result = await _controller.GetProducts(null);

        result.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task GetProducts_PassesFiltersToRepoAndCountsFilteredPages()
    {
        var categoryId = TestData.Faker.Random.Guid();
        var filter = new ProductFilter("cheese", "DAI", "3801001000375", categoryId, true);
        _repo.GetAllProductsAsync(filter, null, 1).Returns([InventoryBuilder.Product()]);
        _repo.GetAllProductsCountAsync(filter).Returns(3);

        var result = await _controller.GetProducts(
            null,
            1,
            "cheese",
            "DAI",
            "3801001000375",
            categoryId,
            true
        );

        result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<ProductPageReadDto>()
            .Pages.ShouldBe(3);
    }

    [Fact]
    public async Task CreateProduct_DuplicateSku_Returns409WithoutSaving()
    {
        var dto = InventoryBuilder.ProductCreateDto();
        _repo.SkuExistsAsync(dto.SKU, null).Returns(true);

        var result = await _controller.CreateProduct(dto);

        result
            .Result.ShouldBeOfType<ObjectResult>()
            .Value.ShouldBeOfType<ProblemDetails>()
            .Status.ShouldBe(409);
        await _repo.DidNotReceive().CreateProductAsync(Arg.Any<Product>());
    }

    [Fact]
    public async Task CreateProduct_UnknownCategory_Returns400WithoutSaving()
    {
        var categoryId = TestData.Faker.Random.Guid();
        var dto = InventoryBuilder.ProductCreateDto(categoryId: categoryId);
        _categories.CategoryExistsAsync(categoryId).Returns(false);

        var result = await _controller.CreateProduct(dto);

        var problem = result
            .Result.ShouldBeOfType<ObjectResult>()
            .Value.ShouldBeOfType<ProblemDetails>();
        problem.Status.ShouldBe(400);
        problem.Title.ShouldBe("Unknown category");
        await _repo.DidNotReceive().CreateProductAsync(Arg.Any<Product>());
    }

    [Fact]
    public async Task UpdateProduct_SkuUsedByAnotherProduct_Returns409()
    {
        var id = TestData.Faker.Random.Guid();
        var sku = InventoryBuilder.Sku();
        _repo.SkuExistsAsync(sku, id).Returns(true);

        var result = await _controller.UpdateProduct(
            id,
            InventoryBuilder.ProductUpdateDto(sku: sku)
        );

        result.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(409);
        await _repo.DidNotReceive().UpdateProductAsync(Arg.Any<Product>());
    }

    [Fact]
    public async Task GetProductById_MissingAndExisting()
    {
        var product = InventoryBuilder.Product();
        _repo.GetProductAsync(product.Id).Returns(product);

        (await _controller.GetProductById(Guid.NewGuid())).Result.ShouldBeOfType<NotFoundResult>();
        (await _controller.GetProductById(product.Id))
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<ProductReadDto>()
            .Id.ShouldBe(product.Id);
    }

    [Fact]
    public async Task CreateProduct_SavesAndReturnsCreatedWithoutPublishing()
    {
        var result = await _controller.CreateProduct(InventoryBuilder.ProductCreateDto("New"));

        var created = result.Result.ShouldBeOfType<CreatedAtRouteResult>();
        created.RouteName.ShouldBe(nameof(ProductsController.GetProductById));
        await _repo.Received(1).CreateProductAsync(Arg.Is<Product>(p => p.Name == "New"));
        await _bus.DidNotReceiveWithAnyArgs().PublishNewItemAsync(default!);
    }

    [Fact]
    public async Task CreateProduct_RepoThrows_Returns500()
    {
        _repo.CreateProductAsync(Arg.Any<Product>()).ThrowsAsync(new InvalidOperationException());

        var result = await _controller.CreateProduct(InventoryBuilder.ProductCreateDto());

        result.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task UpdateProduct_Missing_ReturnsNotFound()
    {
        var result = await _controller.UpdateProduct(
            Guid.NewGuid(),
            InventoryBuilder.ProductUpdateDto("x")
        );

        result.Result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UpdateProduct_NameChanged_ReturnsSavedProductAndPublishesName()
    {
        var saved = InventoryBuilder.Product("Renamed");
        var id = saved.Id;
        _repo.UpdateProductAsync(Arg.Any<Product>()).Returns(saved);

        var result = await _controller.UpdateProduct(
            id,
            InventoryBuilder.ProductUpdateDto(name: "Renamed")
        );

        var body = result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<ProductReadDto>();
        body.Name.ShouldBe("Renamed");
        body.Brand.ShouldBe(saved.Brand);
        await _bus.Received(1)
            .PublishNewItemAsync(
                Arg.Is<ItemPublishedDto>(p =>
                    p.Event == "ProductName_Updated"
                    && p.ExternalProductId == id
                    && p.Name == "Renamed"
                )
            );
    }

    [Fact]
    public async Task UpdateProduct_NameUnchanged_DoesNotPublish()
    {
        _repo.UpdateProductAsync(Arg.Any<Product>()).Returns(ci => ci.Arg<Product>());

        await _controller.UpdateProduct(
            Guid.NewGuid(),
            InventoryBuilder.ProductUpdateDto(brand: "Other")
        );

        await _bus.DidNotReceiveWithAnyArgs().PublishNewItemAsync(default!);
    }

    [Fact]
    public async Task DeleteProduct_Missing_ReturnsNotFound()
    {
        (await _controller.DeleteProduct(Guid.NewGuid())).ShouldBeOfType<NotFoundResult>();
        await _bus.DidNotReceiveWithAnyArgs().PublishNewItemAsync(default!);
    }

    [Fact]
    public async Task DeleteProduct_Existing_PublishesProductDeleted()
    {
        var product = InventoryBuilder.Product();
        _repo.DeleteProductAsync(product.Id).Returns(product);

        (await _controller.DeleteProduct(product.Id)).ShouldBeOfType<NoContentResult>();
        await _bus.Received(1)
            .PublishNewItemAsync(
                Arg.Is<ItemPublishedDto>(p =>
                    p.Event == "Product_Deleted" && p.ExternalProductId == product.Id
                )
            );
    }

    [Fact]
    public async Task DeleteProduct_PublishFails_StillReturnsNoContent()
    {
        var product = InventoryBuilder.Product();
        _repo.DeleteProductAsync(product.Id).Returns(product);
        _bus.PublishNewItemAsync(Arg.Any<ItemPublishedDto>())
            .ThrowsAsync(new InvalidOperationException());

        (await _controller.DeleteProduct(product.Id)).ShouldBeOfType<NoContentResult>();
    }
}
