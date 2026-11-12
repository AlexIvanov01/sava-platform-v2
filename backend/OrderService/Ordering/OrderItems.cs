using System;
using System.Collections.Generic;
using System.Linq;
using OrderService.Dtos;
using OrderService.Models;
using OrderService.SyncDataServices.Grpc;

namespace OrderService.Ordering;

// Turns a requested item list into the order's items. Snapshots are taken only for what is new:
// a product new to the order, or a batch new to its item. Everything else keeps the data it was
// saved with.
public static class OrderItems
{
    // The ids InventoryService has to be asked about for this request.
    public static (List<Guid> ProductIds, List<Guid> BatchIds) SnapshotIds(
        IEnumerable<OrderItem> current,
        IEnumerable<OrderItemWriteDto> requested
    )
    {
        var items = current.ToDictionary(l => l.ProductId);
        var productIds = new List<Guid>();
        var batchIds = new List<Guid>();

        foreach (var request in requested)
        {
            items.TryGetValue(request.ProductId!.Value, out var item);
            if (item == null)
            {
                productIds.Add(request.ProductId.Value);
            }
            batchIds.AddRange(
                request
                    .Batches!.Select(b => b.BatchId!.Value)
                    .Where(id => item == null || item.Batches.TrueForAll(b => b.BatchId != id))
            );
        }

        return (productIds, batchIds);
    }

    // Validates the request against the snapshot, then makes order.Items match it. Returns the
    // problems found; the order is left untouched when there are any.
    public static List<string> Apply(
        Order order,
        IReadOnlyList<OrderItemWriteDto> requested,
        CatalogSnapshot snapshot
    )
    {
        var errors = Validate(order, requested, snapshot);
        if (errors.Count > 0)
        {
            return errors;
        }

        var requestedProducts = requested.Select(r => r.ProductId!.Value).ToHashSet();
        order.Items.RemoveAll(l => !requestedProducts.Contains(l.ProductId));

        foreach (var request in requested)
        {
            var productId = request.ProductId!.Value;
            var item = order.Items.SingleOrDefault(l => l.ProductId == productId);
            if (item == null)
            {
                var product = snapshot.Products[productId];
                item = new OrderItem
                {
                    ProductId = productId,
                    SKU = product.SKU,
                    Name = product.Name,
                    UnitOfMeasure = product.UnitOfMeasure,
                    ListPrice = product.ListPrice,
                    UnitPrice = product.ListPrice,
                    VatRate = product.VatRate,
                    Cost = product.Cost,
                };
                order.Items.Add(item);
            }
            item.UnitPrice = request.UnitPrice ?? item.UnitPrice;

            var quantities = request.Batches!.ToDictionary(
                b => b.BatchId!.Value,
                b => b.Quantity!.Value
            );
            item.Batches.RemoveAll(b => !quantities.ContainsKey(b.BatchId));
            foreach (var (batchId, quantity) in quantities)
            {
                var itemBatch = item.Batches.SingleOrDefault(b => b.BatchId == batchId);
                if (itemBatch == null)
                {
                    var batch = snapshot.Batches[batchId];
                    itemBatch = new OrderItemBatch
                    {
                        BatchId = batchId,
                        Lot = batch.Lot,
                        ExpirationDate = batch.ExpirationDate,
                    };
                    item.Batches.Add(itemBatch);
                }
                itemBatch.Quantity = quantity;
            }
        }

        return errors;
    }

    private static List<string> Validate(
        Order order,
        IReadOnlyList<OrderItemWriteDto> requested,
        CatalogSnapshot snapshot
    )
    {
        var errors = new List<string>();
        foreach (var request in requested)
        {
            var productId = request.ProductId!.Value;
            var item = order.Items.SingleOrDefault(l => l.ProductId == productId);
            if (item == null)
            {
                if (!snapshot.Products.TryGetValue(productId, out var product))
                {
                    errors.Add($"Product {productId} does not exist.");
                    continue;
                }
                if (request.UnitPrice == null && product.ListPrice == null)
                {
                    errors.Add(
                        $"Product {productId} has no list price, so a UnitPrice is required."
                    );
                }
            }

            foreach (var batchId in request.Batches!.Select(b => b.BatchId!.Value))
            {
                if (item != null && item.Batches.Exists(b => b.BatchId == batchId))
                {
                    continue;
                }
                if (!snapshot.Batches.TryGetValue(batchId, out var batch))
                {
                    errors.Add($"Batch {batchId} does not exist.");
                }
                else if (batch.ProductId != productId)
                {
                    errors.Add($"Batch {batchId} belongs to another product than {productId}.");
                }
            }
        }
        return errors;
    }
}
