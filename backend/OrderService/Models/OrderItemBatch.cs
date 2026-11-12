using System;
using System.ComponentModel.DataAnnotations;

namespace OrderService.Models;

// Units of a item taken from one InventoryService batch. Lot and ExpirationDate are a snapshot
// taken when the batch was added to the item.
public class OrderItemBatch
{
    [Key]
    public Guid Id { get; set; }

    public Guid OrderItemId { get; set; }
    public OrderItem? Item { get; set; }

    public Guid BatchId { get; set; }

    [MaxLength(50)]
    public string? Lot { get; set; }

    public DateTime? ExpirationDate { get; set; }

    public int Quantity { get; set; }
}
