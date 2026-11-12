using System;
using System.Collections.Generic;

namespace OrderService.SyncDataServices.Grpc;

// Current InventoryService data for the products and batches an order save adds. Ids that don't
// exist are missing from the dictionaries.
public record CatalogSnapshot(
    IReadOnlyDictionary<Guid, ProductSnapshot> Products,
    IReadOnlyDictionary<Guid, BatchSnapshot> Batches
);

public record ProductSnapshot(
    Guid Id,
    string? SKU,
    string? Name,
    string? UnitOfMeasure,
    decimal? ListPrice,
    decimal? VatRate,
    decimal? Cost
);

public record BatchSnapshot(Guid Id, Guid ProductId, string? Lot, DateTime? ExpirationDate);
