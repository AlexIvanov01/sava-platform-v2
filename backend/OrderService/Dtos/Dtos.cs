using System;

namespace OrderService.Dtos;

public record AuditReadDto(
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    string? UpdatedBy
);

public record InvoiceReadDto(
    int Id,
    DateTime InvoiceDate,
    Guid OrderId,
    string? InvoiceStatus,
    decimal TotalAmount,
    AuditReadDto Audit
);

public record InvoiceCreateDto(
    int? ID,
    DateTime? InvoiceDate,
    string InvoiceStatus,
    decimal? TotalAmount
);

public record InvoiceUpdateDto(DateTime? InvoiceDate, string? InvoiceStatus, decimal? TotalAmount);
