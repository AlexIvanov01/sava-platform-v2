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

public record InvoiceUpdateDto(DateTime? InvoiceDate, string? InvoiceStatus, decimal? TotalAmount);
