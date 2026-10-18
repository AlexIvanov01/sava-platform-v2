using System;
using System.ComponentModel.DataAnnotations;

namespace InventoryService.Models;

public class ProductBatch
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(50)]
    public string? Lot { get; set; }
    public int? Quantity { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? SellPrice { get; set; }
    public DateTime? BatchDateAdded { get; set; }
    public DateTime? BatchDateUpdated { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public Guid? ProductId { get; set; }
    public Product Product { get; set; } = new();
}
