using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InventoryService.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.DataAccess;

public class StockRepo(ProductContext context) : IStockRepo
{
    public async Task<IReadOnlyList<Guid>> SetOrderStockAsync(
        Guid orderId,
        IReadOnlyDictionary<Guid, int> soldPerBatch
    )
    {
        // What the ledger already counts as sold to this order, per batch.
        var alreadySold = await context
            .StockMovements.Where(m =>
                m.OrderId == orderId
                && (m.Type == StockMovementType.Sale || m.Type == StockMovementType.SaleReversal)
            )
            .GroupBy(m => m.BatchId)
            .Select(g => new { BatchId = g.Key, Sold = -g.Sum(m => m.Quantity) })
            .ToDictionaryAsync(x => x.BatchId, x => x.Sold);

        var batchIds = soldPerBatch.Keys.Union(alreadySold.Keys).ToList();
        var batches = await context
            .ProductBatches.Where(b => batchIds.Contains(b.Id))
            .ToDictionaryAsync(b => b.Id);

        await using var transaction = await context.Database.BeginTransactionAsync();

        var unknown = new List<Guid>();
        foreach (var batchId in batchIds)
        {
            if (!batches.TryGetValue(batchId, out var batch))
            {
                unknown.Add(batchId);
                continue;
            }

            var difference =
                soldPerBatch.GetValueOrDefault(batchId) - alreadySold.GetValueOrDefault(batchId);
            if (difference > 0)
            {
                batch.Move(StockMovementType.Sale, -difference, orderId: orderId);
            }
            else if (difference < 0)
            {
                batch.Move(StockMovementType.SaleReversal, -difference, orderId: orderId);
            }
        }

        await context.SaveChangesAsync();
        await RefreshHasOrdersAsync(batches.Values);
        await transaction.CommitAsync();

        return unknown;
    }

    // Recomputes the stored HasOrders flags from the (already saved) ledger.
    private async Task RefreshHasOrdersAsync(ICollection<ProductBatch> batches)
    {
        if (batches.Count == 0)
        {
            return;
        }

        var batchIds = batches.Select(b => b.Id).ToList();
        var withOrders = (
            await context
                .StockMovements.Where(m =>
                    batchIds.Contains(m.BatchId)
                    && m.OrderId != null
                    && (
                        m.Type == StockMovementType.Sale || m.Type == StockMovementType.SaleReversal
                    )
                )
                .GroupBy(m => new { m.BatchId, m.OrderId })
                .Where(g => g.Sum(m => m.Quantity) != 0)
                .Select(g => g.Key.BatchId)
                .Distinct()
                .ToListAsync()
        ).ToHashSet();

        foreach (var batch in batches)
        {
            batch.HasOrders = withOrders.Contains(batch.Id);
        }
        await context.SaveChangesAsync();

        var productIds = batches.Select(b => b.ProductId).Distinct().ToList();
        var products = await context.Products.Where(p => productIds.Contains(p.Id)).ToListAsync();
        var productsWithOrders = (
            await context
                .ProductBatches.Where(b => productIds.Contains(b.ProductId) && b.HasOrders)
                .Select(b => b.ProductId)
                .Distinct()
                .ToListAsync()
        ).ToHashSet();

        foreach (var product in products)
        {
            product.HasOrders = productsWithOrders.Contains(product.Id);
        }
        await context.SaveChangesAsync();
    }

    public async Task<bool> OrderHasMovementsAsync(Guid orderId) =>
        await context.StockMovements.AnyAsync(m => m.OrderId == orderId);

    public async Task<StockMovement?> AdjustAsync(
        Guid batchId,
        int quantity,
        AdjustmentReason reason,
        string? note
    )
    {
        var batch = await context.ProductBatches.SingleOrDefaultAsync(b => b.Id == batchId);

        if (batch == null)
        {
            return null;
        }

        var movement = batch.Move(StockMovementType.Adjustment, quantity, reason, note: note);
        await context.SaveChangesAsync();

        return movement;
    }

    public async Task<IEnumerable<StockMovement>?> GetBatchMovementsAsync(
        Guid batchId,
        DateTime? from,
        DateTime? to
    )
    {
        if (!await context.ProductBatches.AnyAsync(b => b.Id == batchId))
        {
            return null;
        }

        return await InRange(context.StockMovements.Where(m => m.BatchId == batchId), from, to)
            .Include(m => m.Batch)
            .ToListAsync();
    }

    public async Task<IEnumerable<StockMovement>?> GetProductMovementsAsync(
        Guid productId,
        DateTime? from,
        DateTime? to
    )
    {
        if (!await context.Products.AnyAsync(p => p.Id == productId))
        {
            return null;
        }

        return await InRange(
                context.StockMovements.Where(m => m.Batch!.ProductId == productId),
                from,
                to
            )
            .Include(m => m.Batch)
            .ToListAsync();
    }

    private static IQueryable<StockMovement> InRange(
        IQueryable<StockMovement> query,
        DateTime? from,
        DateTime? to
    )
    {
        if (from != null)
        {
            query = query.Where(m => m.CreatedAt >= from);
        }
        if (to != null)
        {
            query = query.Where(m => m.CreatedAt <= to);
        }
        return query.AsNoTracking().OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id);
    }
}
