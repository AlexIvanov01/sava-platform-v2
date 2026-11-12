using InventoryService.Dtos;
using InventoryService.Models;

namespace InventoryService.Mappings;

public static class StockMappings
{
    // Lot is filled when the movement's Batch is loaded.
    public static BatchOrderReadDto ToReadDto(this OrderHolding holding) =>
        new(holding.OrderId, holding.BatchId, holding.Quantity);

    public static StockMovementReadDto ToReadDto(this StockMovement movement) =>
        new(
            movement.Id,
            movement.BatchId,
            movement.Batch?.Lot,
            movement.Type,
            movement.Reason,
            movement.Quantity,
            movement.BalanceAfter,
            movement.OrderId,
            movement.Note,
            movement.CreatedAt,
            movement.CreatedBy
        );
}
