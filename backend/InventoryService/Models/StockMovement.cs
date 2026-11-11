using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace InventoryService.Models;

// One change to a batch's quantity. The ledger is append-only, and a batch's Quantity always
// equals the sum of its movements. CreatedAt/CreatedBy record when and by whom.
public class StockMovement : AuditableEntity
{
    [Key]
    public Guid Id { get; set; }

    public Guid BatchId { get; set; }
    public ProductBatch? Batch { get; set; }

    public StockMovementType Type { get; set; }

    // Only for Adjustment movements.
    public AdjustmentReason? Reason { get; set; }

    // Signed: positive adds stock, negative removes it.
    public int Quantity { get; set; }

    // The batch's quantity right after this movement.
    public int BalanceAfter { get; set; }

    // Set for Sale and SaleReversal.
    public Guid? OrderId { get; set; }

    [MaxLength(500)]
    public string? Note { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<StockMovementType>))]
public enum StockMovementType
{
    // The quantity a batch had when the ledger was introduced.
    OpeningBalance,
    Receipt,
    Sale,
    SaleReversal,
    Adjustment,
}

[JsonConverter(typeof(JsonStringEnumConverter<AdjustmentReason>))]
public enum AdjustmentReason
{
    Damage,
    Shrinkage,
    Expiry,
    CountCorrection,
    Other,
}
