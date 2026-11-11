using System;
using System.Collections.Generic;
using System.Linq;
using InventoryService.Dtos;
using InventoryService.Models;

namespace InventoryService.Reports;

// Report calculations over loaded products and batches. They reuse the domain rules
// (ProductBatch.IsExpired, Product.AvailableQuantity) so reports agree with product read DTOs.
public static class StockReports
{
    public const string InactiveStatus = "Inactive";

    // Products at or below their reorder level, biggest shortfall first. Products without a
    // reorder level, and inactive ones, aren't reordered and are left out.
    public static List<LowStockItemDto> LowStock(IEnumerable<Product> products, DateOnly today) =>
        products
            .Where(p => p.ReorderLevel != null && p.Status != InactiveStatus)
            .Select(p => new { Product = p, Available = p.AvailableQuantity(today) })
            .Where(x => x.Available <= x.Product.ReorderLevel)
            .Select(x => new LowStockItemDto(
                x.Product.Id,
                x.Product.SKU,
                x.Product.Name,
                x.Product.Category?.Name,
                x.Available,
                x.Product.ReorderLevel!.Value,
                x.Product.ReorderLevel.Value - x.Available
            ))
            .OrderByDescending(x => x.Shortfall)
            .ThenBy(x => x.Name)
            .ToList();

    // Batches with stock that expire within `days` (today counts as day 0), soonest first.
    // With includeExpired, batches that already expired but still hold stock are included.
    // Expects each batch's Product to be loaded.
    public static List<ExpiringBatchDto> Expiring(
        IEnumerable<ProductBatch> batches,
        DateOnly today,
        int days,
        bool includeExpired
    ) =>
        batches
            .Where(b => b.Quantity > 0 && b.ExpirationDate != null)
            .Select(b => new
            {
                Batch = b,
                DaysLeft = DateOnly.FromDateTime(b.ExpirationDate!.Value).DayNumber
                    - today.DayNumber,
            })
            .Where(x => x.DaysLeft <= days && (includeExpired || x.DaysLeft >= 0))
            .OrderBy(x => x.Batch.ExpirationDate)
            .ThenBy(x => x.Batch.Product.Name)
            .Select(x => new ExpiringBatchDto(
                x.Batch.Id,
                x.Batch.Product.Id,
                x.Batch.Product.SKU,
                x.Batch.Product.Name,
                x.Batch.Lot,
                x.Batch.Location,
                x.Batch.Quantity!.Value,
                x.Batch.ExpirationDate!.Value,
                x.DaysLeft,
                ValueAtCost(x.Batch)
            ))
            .ToList();

    // Products without physical stock are left out; Products is ordered by value, highest first.
    public static StockValuationDto Valuation(IEnumerable<Product> products, DateOnly today)
    {
        var rows = products
            .Select(p =>
            {
                var onHand = p.Batches.Where(b => b.Quantity > 0).ToList();
                var current = onHand.Where(b => !b.IsExpired(today)).ToList();
                var expired = onHand.Where(b => b.IsExpired(today)).ToList();
                return new ProductValuationDto(
                    p.Id,
                    p.SKU,
                    p.Name,
                    p.CategoryId,
                    p.Category?.Name,
                    current.Sum(b => b.Quantity!.Value),
                    current.Sum(ValueAtCost),
                    expired.Sum(b => b.Quantity!.Value),
                    expired.Sum(ValueAtCost)
                );
            })
            .Where(r => r.Quantity > 0 || r.ExpiredQuantity > 0)
            .OrderByDescending(r => r.ValueAtCost)
            .ThenBy(r => r.Name)
            .ToList();

        return new StockValuationDto(
            today,
            rows.Sum(r => r.Quantity),
            rows.Sum(r => r.ValueAtCost),
            rows.Sum(r => r.ExpiredQuantity),
            rows.Sum(r => r.ExpiredValueAtCost),
            rows
        );
    }

    private static decimal ValueAtCost(ProductBatch batch) =>
        (batch.Quantity ?? 0) * (batch.PurchasePrice ?? 0m);
}
