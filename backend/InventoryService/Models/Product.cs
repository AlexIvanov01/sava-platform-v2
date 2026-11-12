using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.Models;

public class Product : AuditableEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(100)]
    public string? Name { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    [MaxLength(50)]
    public string? Brand { get; set; }

    [MaxLength(50)]
    public string? Supplier { get; set; }

    [MaxLength(200)]
    public string? ImageURL { get; set; }

    [MaxLength(50)]
    public string? Status { get; set; }

    [MaxLength(20)]
    [Column(TypeName = "varchar(20)")]
    public string? Barcode { get; set; }

    // Unique (see ProductContext); required by the create DTO.
    [MaxLength(50)]
    [Column(TypeName = "varchar(50)")]
    public string? SKU { get; set; }

    public UnitOfMeasure? UnitOfMeasure { get; set; }

    // Visible in the future online catalog.
    public bool? IsPublished { get; set; }

    // EUR, VAT exclusive. The default price for new order items.
    [Precision(18, 2)]
    public decimal? ListPrice { get; set; }

    // Percent, e.g. 20.00 for the Bulgarian standard rate.
    [Precision(5, 2)]
    public decimal? VatRate { get; set; }

    public int? ReorderLevel { get; set; }
    public float? Weight { get; set; }
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }

    // True while any of its batches HasOrders. Maintained with them; blocks deleting the product.
    public bool HasOrders { get; set; }

    public List<ProductBatch> Batches { get; set; } = [];

    // Highest purchase price among active batches; null when there are none (sold out or expired).
    // Expects Batches to be loaded.
    public decimal? Cost(DateOnly today) =>
        Batches.Where(b => b.IsActive(today)).Max(b => b.PurchasePrice);

    // Stock that can still be sold: every non-expired batch, so oversold (negative) batches
    // reduce it. Expects Batches to be loaded.
    public int AvailableQuantity(DateOnly today) =>
        Batches.Where(b => !b.IsExpired(today)).Sum(b => b.Quantity ?? 0);
}
