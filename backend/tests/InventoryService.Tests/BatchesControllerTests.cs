using InventoryService.Controllers;
using InventoryService.DataAccess;
using InventoryService.Dtos;
using InventoryService.Models;
using Microsoft.AspNetCore.Mvc;
using NSubstitute.ExceptionExtensions;

namespace InventoryService.Tests;

public class BatchesControllerTests
{
    private readonly IProductRepo _repo = Substitute.For<IProductRepo>();
    private readonly BatchesController _controller;

    public BatchesControllerTests() =>
        _controller = new BatchesController(_repo, TimeProvider.System);

    [Fact]
    public async Task CreateBatch_UnknownProduct_ReturnsNotFound()
    {
        var result = await _controller.CreateBatch(
            InventoryBuilder.BatchCreateDto(),
            Guid.NewGuid()
        );

        result.Result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task CreateBatch_ExistingProduct_ReturnsCreatedWithProduct()
    {
        var product = InventoryBuilder.Product("Widget");
        _repo
            .AddBatchAsync(Arg.Any<ProductBatch>(), product.Id)
            .Returns(ci =>
            {
                product.Batches.Add(ci.Arg<ProductBatch>());
                return product;
            });

        var dto = InventoryBuilder.BatchCreateDto("LOT-7");

        var result = await _controller.CreateBatch(dto, product.Id);

        result
            .Result.ShouldBeOfType<CreatedAtRouteResult>()
            .Value.ShouldBeOfType<ProductReadDto>()
            .Batches.ShouldHaveSingleItem()
            .Lot.ShouldBe("LOT-7");
    }

    [Fact]
    public async Task CreateBatch_RepoThrows_Returns500()
    {
        _repo
            .AddBatchAsync(Arg.Any<ProductBatch>(), Arg.Any<Guid>())
            .ThrowsAsync(new InvalidOperationException());

        var result = await _controller.CreateBatch(
            InventoryBuilder.BatchCreateDto(),
            Guid.NewGuid()
        );

        result.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task UpdateBatch_Missing_ReturnsNotFound()
    {
        var result = await _controller.UpdateBatch(
            Guid.NewGuid(),
            InventoryBuilder.BatchUpdateDto(lot: "x")
        );

        result.Result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task UpdateBatch_Existing_ReturnsSavedBatch()
    {
        var saved = InventoryBuilder.Batch();
        _repo.UpdateBatchAsync(Arg.Any<ProductBatch>()).Returns(saved);

        var result = await _controller.UpdateBatch(
            saved.Id,
            InventoryBuilder.BatchUpdateDto(quantity: 3)
        );

        var body = result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<BatchReadDto>();
        body.Id.ShouldBe(saved.Id);
        body.Lot.ShouldBe(saved.Lot);
        body.Quantity.ShouldBe(saved.Quantity!.Value);
        body.PurchasePrice.ShouldBe(saved.PurchasePrice!.Value);
        body.ExpirationDate.ShouldBe(saved.ExpirationDate!.Value);
        body.Location.ShouldBe(saved.Location);
        await _repo
            .Received(1)
            .UpdateBatchAsync(Arg.Is<ProductBatch>(b => b.Id == saved.Id && b.Quantity == 3));
    }

    [Fact]
    public async Task DeleteBatch_MissingAndExisting()
    {
        var batch = InventoryBuilder.Batch();
        _repo.DeleteBatchAsync(batch.Id).Returns((DeleteStatus.Deleted, batch));

        (await _controller.DeleteBatch(Guid.NewGuid())).ShouldBeOfType<NotFoundResult>();
        (await _controller.DeleteBatch(batch.Id)).ShouldBeOfType<NoContentResult>();
    }

    [Fact]
    public async Task DeleteBatch_RepoThrows_Returns500()
    {
        _repo.DeleteBatchAsync(Arg.Any<Guid>()).ThrowsAsync(new InvalidOperationException());

        (await _controller.DeleteBatch(Guid.NewGuid()))
            .ShouldBeOfType<ObjectResult>()
            .StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task DeleteBatch_WithOrders_Returns409()
    {
        var batch = InventoryBuilder.Batch();
        _repo.DeleteBatchAsync(batch.Id).Returns((DeleteStatus.HasOrders, batch));

        var result = await _controller.DeleteBatch(batch.Id);

        result
            .ShouldBeOfType<ObjectResult>()
            .Value.ShouldBeOfType<ProblemDetails>()
            .Title.ShouldBe("Batch has orders");
    }
}
