using System;
using System.Collections.Generic;

namespace InventoryService.Dtos;

public record LowStockItemDto(
    Guid ProductId,
    string? SKU,
    string? Name,
    string? CategoryName,
    int AvailableQuantity,
    int ReorderLevel,
    // How many units are missing to get back to the reorder level.
    int Shortfall
);

public record ExpiringBatchDto(
    Guid BatchId,
    Guid ProductId,
    string? SKU,
    string? ProductName,
    string? Lot,
    string? Location,
    int Quantity,
    DateTime ExpirationDate,
    // Negative once the batch has expired.
    int DaysUntilExpiry,
    decimal ValueAtCost
);

// Only physical stock is valued (batches with a positive quantity), each batch at its own
// PurchasePrice. Expired stock is reported separately.
public record ProductValuationDto(
    Guid ProductId,
    string? SKU,
    string? Name,
    Guid? CategoryId,
    string? CategoryName,
    int Quantity,
    decimal ValueAtCost,
    int ExpiredQuantity,
    decimal ExpiredValueAtCost
);

public record StockValuationDto(
    DateOnly AsOf,
    int Quantity,
    decimal ValueAtCost,
    int ExpiredQuantity,
    decimal ExpiredValueAtCost,
    List<ProductValuationDto> Products
);
