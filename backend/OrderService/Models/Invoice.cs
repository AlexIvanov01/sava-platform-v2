using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace OrderService.Models;

// A fiscal invoice, created when its order is completed (Invoicing/InvoiceFactory). Supplier,
// customer, items and totals are frozen then; only the number, dates, payment fields and the VAT
// exemption reason can change afterwards (Invoicing/InvoiceChanges).
public class Invoice : AuditableEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }
    public Order? Order { get; set; }

    // 10 digits with leading zeros. Not unique in the database: it can be edited freely, and
    // GET /api/Invoices/numbering reports whether a number is taken.
    [MaxLength(10)]
    [Column(TypeName = "varchar(10)")]
    public string? Number { get; set; }

    public DateTime? InvoiceDate { get; set; }

    // Дата на данъчното събитие; defaults to the invoice date.
    public DateTime? TaxEventDate { get; set; }
    public DateTime? DueDate { get; set; }

    [MaxLength(20)]
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.BankTransfer;

    [MaxLength(20)]
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Unpaid;

    // Set while Paid.
    public DateTime? PaidDate { get; set; }

    // Legal basis for not charging VAT on 0% items.
    [MaxLength(500)]
    public string? VatExemptionReason { get; set; }

    public SupplierSnapshot Supplier { get; set; } = new();
    public CustomerSnapshot Customer { get; set; } = new();
    public List<InvoiceItem> Items { get; set; } = [];

    // Net and VAT per VAT rate.
    public List<InvoiceVatTotal> VatTotals { get; set; } = [];

    [Precision(18, 2)]
    public decimal Net { get; set; }

    [Precision(18, 2)]
    public decimal Vat { get; set; }

    [Precision(18, 2)]
    public decimal Gross { get; set; }

    // Unpaid and due before today.
    public bool IsOverdue(DateOnly today) =>
        PaymentStatus == PaymentStatus.Unpaid
        && DueDate is { } due
        && DateOnly.FromDateTime(due) < today;
}

[JsonConverter(typeof(JsonStringEnumConverter<PaymentMethod>))]
public enum PaymentMethod
{
    BankTransfer,
    Cash,
    Card,
}

[JsonConverter(typeof(JsonStringEnumConverter<PaymentStatus>))]
public enum PaymentStatus
{
    Unpaid,
    Paid,
}
