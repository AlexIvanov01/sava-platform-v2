using InventoryService.AsyncDataServices;
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
    private readonly IMessageBusClient _bus = Substitute.For<IMessageBusClient>();
    private readonly BatchesController _controller;

    public BatchesControllerTests() => _controller = new BatchesController(_repo, _bus);

    [Fact]
    public async Task CreateBatch_UnknownProduct_ReturnsNotFound()
    {
        var result = await _controller.CreateBatch(
            InventoryBuilder.BatchCreateDto(),
            Guid.NewGuid()
        );

        result.Result.ShouldBeOfType<NotFoundResult>();
        await _bus.DidNotReceiveWithAnyArgs().PublishNewItemAsync(default!);
    }

    [Fact]
    public async Task CreateBatch_ExistingProduct_ReturnsCreatedAndPublishesItemPublished()
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
        await _bus.Received(1)
            .PublishNewItemAsync(
                Arg.Is<ItemPublishedDto>(p =>
                    p.Event == "Item_Published"
                    && p.ExternalProductId == product.Id
                    && p.Name == "Widget"
                    && p.Lot == "LOT-7"
                    && p.Price == dto.SellPrice
                )
            );
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

    public static TheoryData<string?, decimal?, DateTime?> SyncedFieldChanges =>
        new()
        {
            { "LOT-2", null, null },
            { null, 9.99m, null },
            { null, null, new DateTime(2031, 1, 1, 0, 0, 0, DateTimeKind.Unspecified) },
        };

    [Theory]
    [MemberData(nameof(SyncedFieldChanges))]
    public async Task UpdateBatch_SyncedFieldChanged_PublishesItemUpdated(
        string? lot,
        decimal? sellPrice,
        DateTime? expirationDate
    )
    {
        var saved = InventoryBuilder.Batch();
        var id = saved.Id;
        _repo.UpdateBatchAsync(Arg.Any<ProductBatch>()).Returns(saved);

        var result = await _controller.UpdateBatch(
            id,
            InventoryBuilder.BatchUpdateDto(
                lot: lot,
                sellPrice: sellPrice,
                expirationDate: expirationDate
            )
        );

        var body = result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<BatchReadDto>();
        body.Lot.ShouldBe(saved.Lot);
        body.Quantity.ShouldBe(saved.Quantity!.Value);
        await _bus.Received(1)
            .PublishNewItemAsync(
                Arg.Is<ItemPublishedDto>(p =>
                    p.Event == "Item_Updated"
                    && p.ExternalBatchId == id
                    && p.Lot == saved.Lot
                    && p.Price == saved.SellPrice
                    && p.ExpirationDate == saved.ExpirationDate
                )
            );
    }

    [Fact]
    public async Task UpdateBatch_OnlyQuantityChanged_DoesNotPublish()
    {
        _repo.UpdateBatchAsync(Arg.Any<ProductBatch>()).Returns(ci => ci.Arg<ProductBatch>());

        var result = await _controller.UpdateBatch(
            Guid.NewGuid(),
            InventoryBuilder.BatchUpdateDto(quantity: 3)
        );

        result.Result.ShouldBeOfType<OkObjectResult>();
        await _bus.DidNotReceiveWithAnyArgs().PublishNewItemAsync(default!);
    }

    [Fact]
    public async Task DeleteBatch_MissingAndExisting()
    {
        var batch = InventoryBuilder.Batch();
        _repo.DeleteBatchAsync(batch.Id).Returns(batch);

        (await _controller.DeleteBatch(Guid.NewGuid())).ShouldBeOfType<NotFoundResult>();
        (await _controller.DeleteBatch(batch.Id)).ShouldBeOfType<NoContentResult>();

        await _bus.Received(1)
            .PublishNewItemAsync(
                Arg.Is<ItemPublishedDto>(p =>
                    p.Event == "Item_Deleted" && p.ExternalBatchId == batch.Id
                )
            );
    }

    [Fact]
    public async Task DeleteBatch_RepoThrows_Returns500()
    {
        _repo.DeleteBatchAsync(Arg.Any<Guid>()).ThrowsAsync(new InvalidOperationException());

        (await _controller.DeleteBatch(Guid.NewGuid()))
            .ShouldBeOfType<ObjectResult>()
            .StatusCode.ShouldBe(500);
    }
}
