using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InventoryService.Dtos;
using InventoryService.EventProcessing;
using InventoryService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

[Trait("Category", "Integration")]
public class StockApiTests(InventoryApiFactory factory) : IClassFixture<InventoryApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DevelopmentSeed_StockEntersTheLedgerAsReceipts()
    {
        var product = (
            await _client.GetFromJsonAsync<ProductPageReadDto>("/api/products?sku=DAI-001", Ct)
        )
            .ShouldNotBeNull()
            .ProductReadDtos.ShouldNotBeNull()
            .Single();

        var movements = await _client.GetFromJsonAsync<List<StockMovementReadDto>>(
            $"/api/products/{product.Id}/movements",
            Ct
        );

        movements.ShouldNotBeNull().Count.ShouldBe(product.Batches.Count);
        foreach (var batch in product.Batches)
        {
            var receipt = movements.Single(m => m.BatchId == batch.Id);
            receipt.Type.ShouldBe(StockMovementType.Receipt);
            receipt.Quantity.ShouldBe(batch.Quantity);
            receipt.Lot.ShouldBe(batch.Lot);
            receipt.CreatedAt.ShouldBe(batch.Audit.CreatedAt);
        }
    }

    [Fact]
    public async Task BatchLifecycle_ReceiptCorrectionAndAdjustmentAreInTheLedger()
    {
        var product = await CreateProductAsync();
        var batch = (
            await (
                await _client.PostAsJsonAsync(
                    $"/api/batches/{product.Id}",
                    InventoryBuilder.BatchCreateDto(quantity: 30),
                    Ct
                )
            ).Content.ReadFromJsonAsync<ProductReadDto>(Ct)
        )
            .ShouldNotBeNull()
            .Batches.Single();

        (
            await _client.PutAsJsonAsync(
                $"/api/batches/{batch.Id}",
                InventoryBuilder.BatchUpdateDto(quantity: 28),
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.OK);

        var note = TestData.Faker.Lorem.Sentence();
        var adjust = await _client.PostAsJsonAsync(
            $"/api/batches/{batch.Id}/adjustments",
            new StockAdjustmentCreateDto(-5, AdjustmentReason.Damage, note),
            Ct
        );
        adjust.StatusCode.ShouldBe(HttpStatusCode.Created);
        adjust
            .Headers.Location.ShouldNotBeNull()
            .ToString()
            .ShouldEndWith($"/api/Batches/{batch.Id}/movements", Case.Insensitive);
        var adjustment = (
            await adjust.Content.ReadFromJsonAsync<StockMovementReadDto>(Ct)
        ).ShouldNotBeNull();
        adjustment.BalanceAfter.ShouldBe(23);
        adjustment.Note.ShouldBe(note);

        var movements = (
            await _client.GetFromJsonAsync<List<StockMovementReadDto>>(
                $"/api/batches/{batch.Id}/movements",
                Ct
            )
        ).ShouldNotBeNull();
        movements
            .Select(m => (m.Type, m.Reason, m.Quantity, m.BalanceAfter))
            .ShouldBe([
                (StockMovementType.Adjustment, AdjustmentReason.Damage, -5, 23),
                (StockMovementType.Adjustment, AdjustmentReason.CountCorrection, -2, 28),
                (StockMovementType.Receipt, null, 30, 30),
            ]);
        (await _client.GetFromJsonAsync<ProductReadDto>($"/api/products/{product.Id}", Ct))
            .ShouldNotBeNull()
            .Batches.Single()
            .Quantity.ShouldBe(23);
    }

    public static TheoryData<int?, string?> InvalidAdjustments =>
        new()
        {
            { 0, nameof(AdjustmentReason.CountCorrection) },
            { 3, nameof(AdjustmentReason.Damage) },
            { 3, nameof(AdjustmentReason.Expiry) },
            { null, nameof(AdjustmentReason.Other) },
            { -1, null },
            { -1, "Theft" },
        };

    [Theory]
    [MemberData(nameof(InvalidAdjustments))]
    public async Task Adjust_InvalidRequest_Returns400(int? quantity, string? reason)
    {
        var product = await CreateProductAsync();
        var batch = (
            await (
                await _client.PostAsJsonAsync(
                    $"/api/batches/{product.Id}",
                    InventoryBuilder.BatchCreateDto(quantity: 10),
                    Ct
                )
            ).Content.ReadFromJsonAsync<ProductReadDto>(Ct)
        )
            .ShouldNotBeNull()
            .Batches.Single();

        var response = await _client.PostAsJsonAsync(
            $"/api/batches/{batch.Id}/adjustments",
            new
            {
                quantity,
                reason,
                note = "x",
            },
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UnknownBatchOrProduct_Returns404()
    {
        var id = TestData.Faker.Random.Guid();

        (
            await _client.PostAsJsonAsync(
                $"/api/batches/{id}/adjustments",
                new StockAdjustmentCreateDto(1, AdjustmentReason.Other, null),
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _client.GetAsync($"/api/batches/{id}/movements", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );
        (await _client.GetAsync($"/api/products/{id}/movements", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );
    }

    [Fact]
    public async Task OrderHoldingStock_BlocksDeletingTheBatchAndProductUntilReleased()
    {
        var product = await CreateProductAsync();
        var batch = (
            await (
                await _client.PostAsJsonAsync(
                    $"/api/batches/{product.Id}",
                    InventoryBuilder.BatchCreateDto(quantity: 10),
                    Ct
                )
            ).Content.ReadFromJsonAsync<ProductReadDto>(Ct)
        )
            .ShouldNotBeNull()
            .Batches.Single();
        batch.HasOrders.ShouldBeFalse();
        var processor = factory.Services.GetRequiredService<IEventProcessor>();
        var orderId = TestData.Faker.Random.Guid();

        await processor.ProcessEvent(
            OrderEvent("Order_Published", orderId, new OrderItemPublishedDto(batch.Id, 4))
        );

        var fetched = (
            await _client.GetFromJsonAsync<ProductReadDto>($"/api/products/{product.Id}", Ct)
        ).ShouldNotBeNull();
        fetched.HasOrders.ShouldBeTrue();
        fetched.Batches.Single().HasOrders.ShouldBeTrue();
        var blockedBatch = await _client.DeleteAsync($"/api/batches/{batch.Id}", Ct);
        blockedBatch.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await blockedBatch.Content.ReadFromJsonAsync<ProblemDetails>(Ct))
            .ShouldNotBeNull()
            .Title.ShouldBe("Batch has orders");
        (await _client.DeleteAsync($"/api/products/{product.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.Conflict
        );

        await processor.ProcessEvent(
            OrderEvent("Order_Deleted", orderId, new OrderItemPublishedDto(batch.Id, 4))
        );

        (await _client.DeleteAsync($"/api/batches/{batch.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );
        (await _client.DeleteAsync($"/api/products/{product.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );
    }

    private static string OrderEvent(
        string eventName,
        Guid orderId,
        params OrderItemPublishedDto[] items
    ) => JsonSerializer.Serialize(new OrderPublishedDto(orderId, items, eventName));

    private async Task<ProductReadDto> CreateProductAsync()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/products",
            InventoryBuilder.ProductCreateDto(),
            Ct
        );
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ProductReadDto>(Ct)).ShouldNotBeNull();
    }
}
