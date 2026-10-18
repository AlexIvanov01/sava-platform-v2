using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryService.Models;

public class Product
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
    public int? ReorderLevel { get; set; }
    public float? Weight { get; set; }
    public DateTime? ProductDateAdded { get; set; }
    public DateTime? ProductDateUpdated { get; set; }
    public List<ProductBatch> Batches { get; set; } = [];
}
