using System;
using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace OrderService.Models;

// One order item as invoiced; amounts are rounded to cents as in Models/OrderFigures.
[Owned]
public class InvoiceItem
{
    public Guid ProductId { get; set; }

    [MaxLength(50)]
    public string? SKU { get; set; }

    [MaxLength(100)]
    public string? Name { get; set; }

    [MaxLength(20)]
    public string? UnitOfMeasure { get; set; }

    public int Quantity { get; set; }

    [Precision(18, 2)]
    public decimal UnitPrice { get; set; }

    [Precision(5, 2)]
    public decimal VatRate { get; set; }

    [Precision(18, 2)]
    public decimal Net { get; set; }

    [Precision(18, 2)]
    public decimal Vat { get; set; }

    [Precision(18, 2)]
    public decimal Gross { get; set; }
}

[Owned]
public class InvoiceVatTotal
{
    [Precision(5, 2)]
    public decimal VatRate { get; set; }

    [Precision(18, 2)]
    public decimal Net { get; set; }

    [Precision(18, 2)]
    public decimal Vat { get; set; }
}
