using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using InventoryService.Models;

namespace InventoryService.DataAccess;

public interface IStockRepo
{
    // Moves stock so that, per batch, exactly soldPerBatch[batch] units count as sold to the order
    // (batches the order no longer lists go back to zero). Only the difference to what the ledger
    // already holds is posted, so replaying the same state is a no-op. HasOrders on the touched
    // batches and their products is updated in the same transaction. Returns batch ids that
    // don't exist; they are skipped.
    Task<IReadOnlyList<Guid>> SetOrderStockAsync(
        Guid orderId,
        IReadOnlyDictionary<Guid, int> soldPerBatch
    );

    Task<bool> OrderHasMovementsAsync(Guid orderId);

    // Null when the batch doesn't exist.
    Task<StockMovement?> AdjustAsync(
        Guid batchId,
        int quantity,
        AdjustmentReason reason,
        string? note
    );

    // Newest first, optionally limited to [from, to]; null when the batch or product doesn't exist.
    Task<IEnumerable<StockMovement>?> GetBatchMovementsAsync(
        Guid batchId,
        DateTime? from,
        DateTime? to
    );
    Task<IEnumerable<StockMovement>?> GetProductMovementsAsync(
        Guid productId,
        DateTime? from,
        DateTime? to
    );
}
