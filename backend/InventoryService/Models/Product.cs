using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
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

    // EUR, VAT exclusive. The default price for new order lines.
    [Precision(18, 2)]
    public decimal? ListPrice { get; set; }

    // Percent, e.g. 20.00 for the Bulgarian standard rate.
    [Precision(5, 2)]
    public decimal? VatRate { get; set; }

    public int? ReorderLevel { get; set; }
    public float? Weight { get; set; }
    public Guid? CategoryId { get; set; }
    public Category? Category { get; set; }
    public List<ProductBatch> Batches { get; set; } = [];
}
