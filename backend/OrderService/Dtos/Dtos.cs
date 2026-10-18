using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace OrderService.Dtos;

public record OrderReadDto(
    Guid Id,
    DateTime OrderDate,
    DateTime? ShippedDate,
    string? ShippingAddress,
    string? OrderStatus,
    Guid CustomerId,
    InvoiceReadDto? Invoice,
    List<OrderItemReadDto>? OrderItems
);

public record CustomerReadDto(
    Guid ExternalId,
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

public record InvoiceReadDto(
    int Id,
    DateTime InvoiceDate,
    Guid OrderId,
    string? InvoiceStatus,
    decimal TotalAmount
);

public record OrderItemReadDto(Guid ItemId, int OrderItemQuantity);

public record OrderItemUpdateDto(
    [Required] Guid ItemId,
    [Required] [Range(1, 1000)] int OrderItemQuantity
);

public record OrderItemPublishedDto(Guid ItemId, int OrderItemQuantity);

public record OrderItemCreateDto(
    [Required] Guid ItemId,
    [Required] [Range(1, 1000)] int OrderItemQuantity
);

public record InvoiceCreateDto(
    int? ID,
    DateTime? InvoiceDate,
    string InvoiceStatus,
    decimal? TotalAmount
);

public record OrderCreateDto(
    DateTime? OrderDate,
    DateTime? ShippedDate,
    string ShippingAddress,
    string OrderStatus,
    [Required] Guid? CustomerId,
    [Required] List<OrderItemCreateDto> OrderItems
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        OrderItemValidation.RejectDuplicateItems(OrderItems?.Select(i => i.ItemId));
}

public record OrderUpdateDto(
    DateTime? OrderDate,
    DateTime? ShippedDate,
    string? ShippingAddress,
    string? OrderStatus,
    Guid? CustomerId,
    List<OrderItemUpdateDto>? OrderItems
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        OrderItemValidation.RejectDuplicateItems(OrderItems?.Select(i => i.ItemId));
}

internal static class OrderItemValidation
{
    public static IEnumerable<ValidationResult> RejectDuplicateItems(IEnumerable<Guid>? itemIds)
    {
        var duplicates = (itemIds ?? [])
            .GroupBy(id => id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicates.Count > 0)
        {
            yield return new ValidationResult(
                $"Each item can appear only once in an order. Duplicate item ids: {string.Join(", ", duplicates)}.",
                [nameof(OrderCreateDto.OrderItems)]
            );
        }
    }
}

public record InvoiceUpdateDto(DateTime? InvoiceDate, string? InvoiceStatus, decimal? TotalAmount);

public record CustomerPublishedDto(
    Guid ExternalId,
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
