using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace OrderService.Dtos;

// Every unit of a item comes from a batch, so the item's quantity is the sum of its batches.
// Stock isn't checked: InventoryService lets batches go negative (see POST /api/Allocations/suggest
// there for a FEFO suggestion with shortages).
public record OrderItemWriteDto(
    [Required] Guid? ProductId,
    // Defaults to the product's list price for a new item and stays unchanged for an existing one.
    [Range(0d, double.MaxValue)] decimal? UnitPrice,
    [Required] [MinLength(1)] List<OrderItemBatchWriteDto>? Batches
);

public record OrderItemBatchWriteDto(
    [Required] Guid? BatchId,
    [Required] [Range(1, int.MaxValue)] int? Quantity
);

public record OrderCreateDto(
    DateTime? OrderDate,
    DateTime? ShippedDate,
    [MaxLength(500)] string? ShippingAddress,
    [MaxLength(50)] string? OrderStatus,
    [Required] Guid? CustomerId,
    [Required] [MinLength(1)] List<OrderItemWriteDto>? Items
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        OrderItemValidation.RejectDuplicates(Items);
}

// Null fields are left unchanged. Items, when given, is the order's complete new item list: items
// for products still on the order keep their snapshot, removed products are dropped, and new ones
// are snapshotted.
public record OrderUpdateDto(
    DateTime? OrderDate,
    DateTime? ShippedDate,
    [MaxLength(500)] string? ShippingAddress,
    [MaxLength(50)] string? OrderStatus,
    Guid? CustomerId,
    [MinLength(1)] List<OrderItemWriteDto>? Items
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        OrderItemValidation.RejectDuplicates(Items);
}

internal static class OrderItemValidation
{
    public static IEnumerable<ValidationResult> RejectDuplicates(List<OrderItemWriteDto>? items)
    {
        var duplicateProducts = Duplicates((items ?? []).Select(l => l.ProductId));
        if (duplicateProducts.Count > 0)
        {
            yield return new ValidationResult(
                $"Each product can appear only once in an order. Duplicate product ids: {string.Join(", ", duplicateProducts)}.",
                ["Items"]
            );
        }

        var duplicateBatches = Duplicates(
            (items ?? []).SelectMany(l => l.Batches ?? []).Select(b => b.BatchId)
        );
        if (duplicateBatches.Count > 0)
        {
            yield return new ValidationResult(
                $"Each batch can appear only once in an order. Duplicate batch ids: {string.Join(", ", duplicateBatches)}.",
                ["Items"]
            );
        }
    }

    private static List<Guid> Duplicates(IEnumerable<Guid?> ids) =>
        ids.OfType<Guid>().GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
}

public record CustomerSnapshotReadDto(
    string? Name,
    string? CompanyName,
    string? Email,
    string? PhoneNumber,
    string? Address,
    string? City,
    string? Country,
    string? BankName,
    string? IBAN,
    string? BIC,
    string? VATNumber,
    string? UIC
);

public record OrderItemBatchReadDto(
    Guid BatchId,
    string? Lot,
    DateTime? ExpirationDate,
    int Quantity
);

// Net = UnitPrice x Quantity, Vat = Net x VatRate, Gross = Net + Vat, each rounded to cents.
// Ebit = Net - Cost x Quantity; null when the product had no cost.
public record OrderItemReadDto(
    Guid ProductId,
    string? SKU,
    string? Name,
    string? UnitOfMeasure,
    int Quantity,
    decimal? ListPrice,
    decimal? UnitPrice,
    decimal? VatRate,
    decimal? Cost,
    decimal Net,
    decimal Vat,
    decimal Gross,
    decimal? Ebit,
    decimal? EbitPercent,
    List<OrderItemBatchReadDto> Batches
);

// Sums of the item amounts; Cost and Ebit are null when any item has no cost.
public record OrderTotalsReadDto(
    decimal Net,
    decimal Vat,
    decimal Gross,
    decimal? Cost,
    decimal? Ebit,
    decimal? EbitPercent
);

public record OrderReadDto(
    Guid Id,
    DateTime? OrderDate,
    DateTime? ShippedDate,
    string? ShippingAddress,
    string? OrderStatus,
    Guid? CustomerId,
    CustomerSnapshotReadDto? Customer,
    InvoiceReadDto? Invoice,
    List<OrderItemReadDto> Items,
    OrderTotalsReadDto Totals,
    AuditReadDto Audit
);
