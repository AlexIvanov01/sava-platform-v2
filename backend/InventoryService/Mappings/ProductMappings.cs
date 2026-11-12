using System;
using System.Globalization;
using System.Linq;
using InventoryService.Dtos;
using InventoryService.Models;

namespace InventoryService.Mappings;

public static class ProductMappings
{
    // Bulgarian standard VAT rate, in percent.
    public const decimal DefaultVatRate = 20m;

    // today decides which batches count as expired for the derived cost and stock figures.
    public static ProductReadDto ToReadDto(this Product product, DateOnly today)
    {
        var cost = product.Cost(today);
        return new(
            product.Id,
            product.Name,
            product.Description,
            product.Brand,
            product.Supplier,
            product.ImageURL,
            product.Status,
            product.Barcode,
            product.ReorderLevel.GetValueOrDefault(),
            product.Weight.GetValueOrDefault(),
            product.SKU,
            product.ListPrice,
            product.VatRate ?? DefaultVatRate,
            product.UnitOfMeasure ?? UnitOfMeasure.Piece,
            product.IsPublished ?? false,
            product.CategoryId,
            product.Category?.Name,
            cost,
            product.AvailableQuantity(today),
            Margin.Ebit(product.ListPrice, cost),
            Margin.EbitPercent(product.ListPrice, cost),
            product.HasOrders,
            product.Batches.Select(b => b.ToReadDto()).ToList(),
            product.ToAuditDto()
        );
    }

    public static BatchReadDto ToReadDto(this ProductBatch batch) =>
        new(
            batch.Id,
            batch.Lot,
            batch.Quantity.GetValueOrDefault(),
            batch.PurchasePrice.GetValueOrDefault(),
            batch.ExpirationDate.GetValueOrDefault(),
            batch.Location,
            batch.HasOrders,
            batch.ToAuditDto()
        );

    public static AuditReadDto ToAuditDto(this AuditableEntity entity) =>
        new(entity.CreatedAt, entity.UpdatedAt, entity.CreatedBy, entity.UpdatedBy);

    public static Product ToEntity(this ProductCreateDto dto) =>
        new()
        {
            Name = dto.Name,
            Description = dto.Description,
            Brand = dto.Brand,
            Supplier = dto.Supplier,
            ImageURL = dto.ImageURL,
            Status = dto.Status,
            Barcode = dto.Barcode,
            ReorderLevel = dto.ReorderLevel,
            Weight = dto.Weight,
            SKU = dto.SKU,
            ListPrice = dto.ListPrice,
            VatRate = dto.VatRate ?? DefaultVatRate,
            UnitOfMeasure = dto.UnitOfMeasure ?? UnitOfMeasure.Piece,
            IsPublished = dto.IsPublished ?? false,
            CategoryId = dto.CategoryId,
        };

    public static Product ToEntity(this ProductUpdateDto dto, Guid id) =>
        new()
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
            Weight = dto.Weight,
            SKU = dto.SKU,
            ListPrice = dto.ListPrice,
            VatRate = dto.VatRate,
            UnitOfMeasure = dto.UnitOfMeasure,
            IsPublished = dto.IsPublished,
            CategoryId = dto.CategoryId,
        };

    public static ProductBatch ToEntity(this BatchCreateDto dto) =>
        new()
        {
            Lot = dto.Lot,
            Quantity = dto.Quantity,
            PurchasePrice = dto.PurchasePrice,
            ExpirationDate = dto.ExpirationDate,
            Location = dto.Location,
        };

    public static ProductBatch ToEntity(this BatchUpdateDto dto, Guid id) =>
        new()
        {
            Id = id,
            Lot = dto.Lot,
            Quantity = dto.Quantity,
            PurchasePrice = dto.PurchasePrice,
            ExpirationDate = dto.ExpirationDate,
            Location = dto.Location,
        };

    public static GrpcProductSnapshot ToSnapshot(this Product product, DateOnly today) =>
        new()
        {
            Id = product.Id.ToString(),
            Sku = product.SKU ?? string.Empty,
            Name = product.Name ?? string.Empty,
            UnitOfMeasure = product.UnitOfMeasure?.ToString() ?? string.Empty,
            ListPrice = product.ListPrice?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            VatRate = product.VatRate?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Cost = product.Cost(today)?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        };

    public static GrpcBatchSnapshot ToSnapshot(this ProductBatch batch) =>
        new()
        {
            Id = batch.Id.ToString(),
            ProductId = batch.ProductId?.ToString() ?? string.Empty,
            Lot = batch.Lot ?? string.Empty,
            ExpirationDate =
                batch.ExpirationDate?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty,
        };
}
