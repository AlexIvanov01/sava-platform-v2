using InventoryService.Dtos;
using InventoryService.Models;

namespace InventoryService.Tests;

internal static class InventoryBuilder
{
    public static Product Product(string name = "Widget", params ProductBatch[] batches) =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "A widget",
            Brand = "Acme",
            Supplier = "Acme Ltd",
            Barcode = "3800000000001",
            ReorderLevel = 5,
            Weight = 1.5f,
            Batches = [.. batches],
        };

    public static ProductBatch Batch(string lot = "LOT-1", int quantity = 10) =>
        new()
        {
            Id = Guid.NewGuid(),
            Lot = lot,
            Quantity = quantity,
            PurchasePrice = 5.50m,
            SellPrice = 12.99m,
            ExpirationDate = new DateTime(2030, 1, 15, 0, 0, 0, DateTimeKind.Unspecified),
        };

    public static ProductCreateDto ProductCreateDto(string name = "Widget") =>
        new(name, "A widget", "Acme", "Acme Ltd", null, "Active", "3800000000001", 5, 1.5f);

    public static ProductUpdateDto ProductUpdateDto(string? name = null, string? brand = null) =>
        new(name, null, brand, null, null, null, null, null, null);

    public static BatchCreateDto BatchCreateDto(string lot = "LOT-1", int quantity = 10) =>
        new(
            lot,
            quantity,
            5.50m,
            12.99m,
            new DateTime(2030, 1, 15, 0, 0, 0, DateTimeKind.Unspecified)
        );

    public static BatchUpdateDto BatchUpdateDto(
        string? lot = null,
        int? quantity = null,
        decimal? sellPrice = null,
        DateTime? expirationDate = null
    ) => new(lot, quantity, null, sellPrice, expirationDate);
}
