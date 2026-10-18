using InventoryService.AsyncDataServices;
using InventoryService.Controllers;
using InventoryService.DataAccess;
using InventoryService.Dtos;
using InventoryService.Models;
using Microsoft.AspNetCore.Mvc;
using NSubstitute.ExceptionExtensions;

namespace InventoryService.Tests;

public class ProductsControllerTests
{
    private readonly IProductRepo _repo = Substitute.For<IProductRepo>();
    private readonly IMessageBusClient _bus = Substitute.For<IMessageBusClient>();
    private readonly ProductsController _controller;

    public ProductsControllerTests() => _controller = new ProductsController(_repo, _bus);

    [Fact]
    public async Task GetProducts_Empty_ReturnsNotFound()
    {
        _repo.GetAllProductsAsync(null, 10).Returns(new List<Product>());

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
        _repo.GetAllProductsAsync(cursor, 2).Returns(products);
        _repo.GetAllProductsCountAsync().Returns(5);

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
            .GetAllProductsAsync(Arg.Any<Guid?>(), Arg.Any<int>())
            .ThrowsAsync(new InvalidOperationException());

        var result = await _controller.GetProducts(null);

        result.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(500);
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
