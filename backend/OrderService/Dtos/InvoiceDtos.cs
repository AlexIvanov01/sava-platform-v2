using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using OrderService.Models;

namespace OrderService.Dtos;

// The invoice as shown on its order.
public record InvoiceSummaryReadDto(
    Guid Id,
    string? Number,
    DateTime? InvoiceDate,
    DateTime? DueDate,
    PaymentStatus PaymentStatus,
    decimal Gross
);

public record SupplierReadDto(
    string? Name,
    string? UIC,
    string? VATNumber,
    string? Address,
    string? City,
    string? Country,
    string? Manager,
    string? BankName,
    string? IBAN,
    string? BIC,
    string? Email,
    string? PhoneNumber
);

public record InvoiceItemReadDto(
    Guid ProductId,
    string? SKU,
    string? Name,
    string? UnitOfMeasure,
    int Quantity,
    decimal UnitPrice,
    decimal VatRate,
    decimal Net,
    decimal Vat,
    decimal Gross
);

public record InvoiceVatTotalReadDto(decimal VatRate, decimal Net, decimal Vat);

// OrderStatus tells whether the order was cancelled after it was invoiced.
public record InvoiceReadDto(
    Guid Id,
    Guid OrderId,
    OrderStatus? OrderStatus,
    string? Number,
    DateTime? InvoiceDate,
    DateTime? TaxEventDate,
    DateTime? DueDate,
    PaymentMethod PaymentMethod,
    PaymentStatus PaymentStatus,
    DateTime? PaidDate,
    bool IsOverdue,
    string? VatExemptionReason,
    SupplierReadDto Supplier,
    CustomerSnapshotReadDto Customer,
    List<InvoiceItemReadDto> Items,
    List<InvoiceVatTotalReadDto> VatTotals,
    decimal Net,
    decimal Vat,
    decimal Gross,
    AuditReadDto Audit
);

// Null fields are left unchanged. Supplier, customer, items and totals can't be changed.
public record InvoiceUpdateDto(
    [RegularExpression(@"^\d{10}$", ErrorMessage = "Number must be 10 digits.")] string? Number,
    DateTime? InvoiceDate,
    DateTime? TaxEventDate,
    DateTime? DueDate,
    PaymentMethod? PaymentMethod,
    PaymentStatus? PaymentStatus,
    DateTime? PaidDate,
    [MaxLength(500)] string? VatExemptionReason
);

public record InvoiceNumberCheckDto(string Number, bool Taken, string NextNumber);

// Query-string filters for GET /api/Invoices; null fields don't filter. From/To are inclusive
// days of InvoiceDate, Number matches a substring, Overdue compares DueDate with today.
public record InvoiceFilter(
    DateOnly? From,
    DateOnly? To,
    Guid? CustomerId,
    string? Number,
    PaymentStatus? PaymentStatus,
    bool? Overdue
);
