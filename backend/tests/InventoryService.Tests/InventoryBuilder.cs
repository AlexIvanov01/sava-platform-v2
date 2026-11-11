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
            // SKUs are unique, so they come from a GUID rather than a small random range.
            SKU = Sku(),
            UnitOfMeasure = f.PickRandom<UnitOfMeasure>(),
            IsPublished = f.Random.Bool(),
            ListPrice = f.Finance.Amount(1, 100),
            VatRate = f.PickRandom(0m, 9m, 20m),
            ReorderLevel = f.Random.Int(1, 50),
            // One decimal place, so the value survives MySQL's single-precision FLOAT column.
            Weight = f.Random.Int(1, 500) / 10f,
            Batches = [.. batches],
        };
    }

    public static string Sku() => $"SKU-{TestData.Faker.Random.Guid():N}"[..20].ToUpperInvariant();

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
            Location = $"Aisle {f.Random.Int(1, 9)}, shelf {f.Random.Char('A', 'F')}",
        };
    }

    public static Category Category(string? name = null)
    {
        var f = TestData.Faker;
        return new()
        {
            Id = f.Random.Guid(),
            // Category names are unique; the GUID suffix keeps generated ones apart.
            Name = name ?? $"{f.Commerce.Department()} {f.Random.Guid():N}".ClampLength(max: 40),
            Description = f.Lorem.Sentence().ClampLength(max: 500),
        };
    }

    public static ProductCreateDto ProductCreateDto(string? name = null, Guid? categoryId = null)
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
            p.Weight,
            p.SKU!,
            p.ListPrice,
            p.VatRate,
            p.UnitOfMeasure,
            p.IsPublished,
            categoryId
        );
    }

    public static ProductUpdateDto ProductUpdateDto(
        string? name = null,
        string? brand = null,
        string? sku = null,
        decimal? listPrice = null,
        Guid? categoryId = null,
        bool? isPublished = null
    ) =>
        new(
            name,
            null,
            brand,
            null,
            null,
            null,
            null,
            null,
            null,
            sku,
            listPrice,
            null,
            null,
            isPublished,
            categoryId
        );

    public static BatchCreateDto BatchCreateDto(string? lot = null, int? quantity = null)
    {
        var b = Batch(lot, quantity);
        return new(b.Lot!, b.Quantity, b.PurchasePrice, b.SellPrice, b.ExpirationDate, b.Location);
    }

    public static BatchUpdateDto BatchUpdateDto(
        string? lot = null,
        int? quantity = null,
        decimal? sellPrice = null,
        DateTime? expirationDate = null,
        string? location = null
    ) => new(lot, quantity, null, sellPrice, expirationDate, location);
}
