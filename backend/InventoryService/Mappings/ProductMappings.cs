using System;
using System.Linq;
using InventoryService.Dtos;
using InventoryService.Models;

namespace InventoryService.Mappings;

public static class ProductMappings
{
    public static ProductReadDto ToReadDto(this Product product) => new(
        product.Id, product.Name, product.Description, product.Brand, product.Supplier,
        product.ImageURL, product.Status, product.Barcode,
        product.ReorderLevel.GetValueOrDefault(), product.Weight.GetValueOrDefault(),
        product.Batches.Select(b => b.ToReadDto()).ToList());

    public static BatchReadDto ToReadDto(this ProductBatch batch) => new(
        batch.Id, batch.Lot, batch.Quantity.GetValueOrDefault(),
        batch.PurchasePrice.GetValueOrDefault(), batch.SellPrice.GetValueOrDefault(),
        batch.ExpirationDate.GetValueOrDefault());

    public static Product ToEntity(this ProductCreateDto dto) => new()
    {
        Name = dto.Name,
        Description = dto.Description,
        Brand = dto.Brand,
        Supplier = dto.Supplier,
        ImageURL = dto.ImageURL,
        Status = dto.Status,
        Barcode = dto.Barcode,
        ReorderLevel = dto.ReorderLevel,
        Weight = dto.Weight
    };

    public static Product ToEntity(this ProductUpdateDto dto, Guid id) => new()
    {
        Id = id,
        Name = dto.Name,
        Description = dto.Description,
        Brand = dto.Brand,
        Supplier = dto.Supplier,
        ImageURL = dto.ImageURL,
        Status = dto.Status,
        Barcode = dto.Barcode,
        ReorderLevel = dto.ReorderLevel,
        Weight = dto.Weight
    };

    public static ProductBatch ToEntity(this BatchCreateDto dto) => new()
    {
        Lot = dto.Lot,
        Quantity = dto.Quantity,
        PurchasePrice = dto.PurchasePrice,
        SellPrice = dto.SellPrice,
        ExpirationDate = dto.ExpirationDate
    };

    public static ProductBatch ToEntity(this BatchUpdateDto dto, Guid id) => new()
    {
        Id = id,
        Lot = dto.Lot,
        Quantity = dto.Quantity,
        PurchasePrice = dto.PurchasePrice,
        SellPrice = dto.SellPrice,
        ExpirationDate = dto.ExpirationDate
    };

    // Expects batch.Product to be populated. Protobuf string fields reject null,
    // so missing values are sent as empty strings.
    public static GrpcInventoryModel ToGrpcModel(this ProductBatch batch) => new()
    {
        ExternalProductId = batch.ProductId.ToString(),
        ExternalBatchId = batch.Id.ToString(),
        Name = batch.Product.Name ?? string.Empty,
        Price = batch.SellPrice.ToString(),
        ExpirationDate = batch.ExpirationDate.ToString(),
        Lot = batch.Lot ?? string.Empty
    };
}
