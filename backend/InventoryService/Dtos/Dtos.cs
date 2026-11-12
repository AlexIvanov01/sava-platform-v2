using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using InventoryService.Models;

namespace InventoryService.Dtos;

public record AuditReadDto(
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    string? UpdatedBy
);

public record BatchReadDto(
    Guid Id,
    string? Lot,
    int Quantity,
    decimal PurchasePrice,
    decimal SellPrice,
    DateTime ExpirationDate,
    string? Location,
    // True while an order holds stock from the batch; it can't be deleted then.
    bool HasOrders,
    AuditReadDto Audit
);

public record ProductReadDto(
    Guid Id,
    string? Name,
    string? Description,
    string? Brand,
    string? Supplier,
    string? ImageURL,
    string? Status,
    string? Barcode,
    int ReorderLevel,
    float Weight,
    string? SKU,
    decimal? ListPrice,
    decimal VatRate,
    UnitOfMeasure UnitOfMeasure,
    bool IsPublished,
    Guid? CategoryId,
    string? CategoryName,
    // Derived from the batches on read: see Product.Cost/AvailableQuantity and Models.Margin.
    decimal? Cost,
    int AvailableQuantity,
    decimal? Ebit,
    decimal? EbitPercent,
    // True while an order holds stock from any of its batches; it can't be deleted then.
    bool HasOrders,
    List<BatchReadDto> Batches,
    AuditReadDto Audit
);

public record ProductCreateDto(
    [Required] string Name,
    string? Description,
    [Required] string Brand,
    [Required] string Supplier,
    string? ImageURL,
    string? Status,
    string? Barcode,
    int? ReorderLevel,
    float? Weight,
    [Required] [MaxLength(50)] string SKU,
    [Required] [Range(0d, double.MaxValue)] decimal? ListPrice,
    [Range(0d, 100d)] decimal? VatRate,
    UnitOfMeasure? UnitOfMeasure,
    bool? IsPublished,
    Guid? CategoryId
);

public record BatchCreateDto(
    [Required] string Lot,
    [Required] int? Quantity,
    [Required] decimal? PurchasePrice,
    [Required] decimal? SellPrice,
    [Required] DateTime? ExpirationDate,
    [MaxLength(100)] string? Location
);

public record ProductUpdateDto(
    string? Name,
    string? Description,
    string? Brand,
    string? Supplier,
    string? ImageURL,
    string? Status,
    string? Barcode,
    int? ReorderLevel,
    float? Weight,
    [MaxLength(50)] string? SKU,
    [Range(0d, double.MaxValue)] decimal? ListPrice,
    [Range(0d, 100d)] decimal? VatRate,
    UnitOfMeasure? UnitOfMeasure,
    bool? IsPublished,
    Guid? CategoryId
);

public record BatchUpdateDto(
    string? Lot,
    int? Quantity,
    decimal? PurchasePrice,
    decimal? SellPrice,
    DateTime? ExpirationDate,
    [MaxLength(100)] string? Location
);

// Query-string filters for GET /api/Products; null fields don't filter.
public record ProductFilter(
    string? Name = null,
    string? SKU = null,
    string? Barcode = null,
    Guid? CategoryId = null,
    bool? IsPublished = null
)
{
    public static readonly ProductFilter None = new();
}

public record CategoryReadDto(
    Guid Id,
    string? Name,
    string? Description,
    int ProductCount,
    AuditReadDto Audit
);

public record CategoryCreateDto(
    [Required] [MaxLength(100)] string Name,
    [MaxLength(500)] string? Description
);

public record CategoryUpdateDto(
    [MaxLength(100)] string? Name,
    [MaxLength(500)] string? Description
);

// OrderId is required; EventProcessor rejects messages without it.
public record OrderPublishedDto(
    Guid? OrderId,
    IEnumerable<OrderItemPublishedDto> OrderItems,
    string Event
);

public record StockMovementReadDto(
    Guid Id,
    Guid BatchId,
    string? Lot,
    StockMovementType Type,
    AdjustmentReason? Reason,
    int Quantity,
    int BalanceAfter,
    Guid? OrderId,
    string? Note,
    DateTime? CreatedAt,
    string? CreatedBy
);

// A manual stock change. Quantity is signed; losses (damage, shrinkage, expiry) must be negative.
public record StockAdjustmentCreateDto(
    [Required] int? Quantity,
    [Required] AdjustmentReason? Reason,
    [MaxLength(500)] string? Note
) : IValidatableObject
{
    private static readonly AdjustmentReason[] Losses =
    [
        AdjustmentReason.Damage,
        AdjustmentReason.Shrinkage,
        AdjustmentReason.Expiry,
    ];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Quantity == 0)
        {
            yield return new ValidationResult("Quantity must not be zero.", [nameof(Quantity)]);
        }
        else if (Quantity > 0 && Reason is { } reason && Losses.Contains(reason))
        {
            yield return new ValidationResult(
                $"A {reason} adjustment removes stock, so Quantity must be negative.",
                [nameof(Quantity)]
            );
        }
    }
}

public record OrderItemPublishedDto(Guid ItemId, int OrderItemQuantity);
