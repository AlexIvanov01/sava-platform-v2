using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace OrderService.Models;

// A product on an order. The product fields are a snapshot taken from InventoryService when the
// product was added to the order; they stay as they are until the item is removed and added
// again. Only UnitPrice and the batches can be edited.
public class OrderItem
{
    [Key]
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }
    public Order? Order { get; set; }

    public Guid ProductId { get; set; }

    [MaxLength(50)]
    public string? SKU { get; set; }

    [MaxLength(100)]
    public string? Name { get; set; }

    [MaxLength(20)]
    public string? UnitOfMeasure { get; set; }

    // EUR, VAT exclusive.
    [Precision(18, 2)]
    public decimal? ListPrice { get; set; }

    // EUR, VAT exclusive; defaults to ListPrice.
    [Precision(18, 2)]
    public decimal? UnitPrice { get; set; }

    // Percent.
    [Precision(5, 2)]
    public decimal? VatRate { get; set; }

    // Per unit: the product's cost (highest purchase price of its active batches) when it was
    // added; null when it had none.
    [Precision(18, 2)]
    public decimal? Cost { get; set; }

    public List<OrderItemBatch> Batches { get; set; } = [];

    [NotMapped]
    public int Quantity => Batches.Sum(b => b.Quantity);
}
