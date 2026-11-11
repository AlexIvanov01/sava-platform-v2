using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace InventoryService.Models;

public class ProductBatch : AuditableEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(50)]
    public string? Lot { get; set; }
    public int? Quantity { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? SellPrice { get; set; }
    public DateTime? ExpirationDate { get; set; }

    // Free-text place in the warehouse, e.g. "Aisle 3, shelf B".
    [MaxLength(100)]
    public string? Location { get; set; }
    public Guid? ProductId { get; set; }
    public Product Product { get; set; } = new();
    public List<StockMovement> Movements { get; set; } = [];

    // The only way stock changes: adjusts Quantity and appends the matching ledger entry.
    public StockMovement Move(
        StockMovementType type,
        int quantity,
        AdjustmentReason? reason = null,
        Guid? orderId = null,
        string? note = null
    )
    {
        Quantity = (Quantity ?? 0) + quantity;
        var movement = new StockMovement
        {
            BatchId = Id,
            Type = type,
            Reason = reason,
            Quantity = quantity,
            BalanceAfter = Quantity.Value,
            OrderId = orderId,
            Note = note,
        };
        Movements.Add(movement);
        return movement;
    }

    // A batch is sellable through its expiration date; no date means it doesn't expire.
    public bool IsExpired(DateOnly today) =>
        ExpirationDate != null && DateOnly.FromDateTime(ExpirationDate.Value) < today;

    // In stock and not expired.
    public bool IsActive(DateOnly today) => Quantity > 0 && !IsExpired(today);
}
