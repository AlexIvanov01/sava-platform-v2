using Bogus;
using Bogus.Extensions;
using InventoryService.Dtos;
using InventoryService.Models;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

// Fake data from Bogus; lengths are clamped to the model's [MaxLength] limits.
internal static class InventoryBuilder
{
    public static Product Product(string? name = null, params ProductBatch[] batches)
    {
        var f = TestData.Faker;
        return new()
        {
            Id = f.Random.Guid(),
            Name = name ?? f.Commerce.ProductName(),
            Description = f.Commerce.ProductDescription().ClampLength(max: 500),
            Brand = f.Company.CompanyName().ClampLength(max: 50),
            Supplier = f.Company.CompanyName().ClampLength(max: 50),
            ImageURL = f.Image.PicsumUrl(),
            Status = f.PickRandom("Active", "Inactive"),
            Barcode = f.Commerce.Ean13(),
            ReorderLevel = f.Random.Int(1, 50),
            // One decimal place, so the value survives MySQL's single-precision FLOAT column.
            Weight = f.Random.Int(1, 500) / 10f,
            Batches = [.. batches],
        };
    }

    public static ProductBatch Batch(string? lot = null, int? quantity = null)
    {
        var f = TestData.Faker;
        var purchasePrice = f.Finance.Amount(1, 50);
        return new()
        {
            Id = f.Random.Guid(),
            Lot = lot ?? f.Random.Replace("LOT-#####"),
            Quantity = quantity ?? f.Random.Int(1, 100),
            PurchasePrice = purchasePrice,
            SellPrice = purchasePrice + f.Finance.Amount(1, 50),
            ExpirationDate = TestData.Date(2027, 2035),
        };
    }

    public static ProductCreateDto ProductCreateDto(string? name = null)
    {
        var p = Product(name);
        return new(
            p.Name!,
            p.Description,
            p.Brand!,
            p.Supplier!,
            p.ImageURL,
            p.Status,
            p.Barcode,
            p.ReorderLevel,
            p.Weight
        );
    }

    public static ProductUpdateDto ProductUpdateDto(string? name = null, string? brand = null) =>
        new(name, null, brand, null, null, null, null, null, null);

    public static BatchCreateDto BatchCreateDto(string? lot = null, int? quantity = null)
    {
        var b = Batch(lot, quantity);
        return new(b.Lot!, b.Quantity, b.PurchasePrice, b.SellPrice, b.ExpirationDate);
    }

    public static BatchUpdateDto BatchUpdateDto(
        string? lot = null,
        int? quantity = null,
        decimal? sellPrice = null,
        DateTime? expirationDate = null
    ) => new(lot, quantity, null, sellPrice, expirationDate);
}
