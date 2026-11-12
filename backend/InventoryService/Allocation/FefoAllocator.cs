using System;
using System.Collections.Generic;
using System.Linq;
using InventoryService.Dtos;
using InventoryService.Models;

namespace InventoryService.Allocation;

// First expired, first out: suggests which batches a quantity of a product should come from.
public static class FefoAllocator
{
    // Expects product.Batches to be loaded. heldByOrder (batch id -> units) is stock the order
    // being edited already holds, which counts as available to it again.
    public static ItemAllocationSuggestionDto Suggest(
        Product product,
        int quantity,
        IReadOnlyDictionary<Guid, int> heldByOrder,
        DateOnly today
    )
    {
        var remaining = quantity;
        var batches = new List<BatchAllocationSuggestionDto>();

        var candidates = product
            .Batches.Where(b => !b.IsExpired(today))
            .OrderBy(b => b.ExpirationDate == null)
            .ThenBy(b => b.ExpirationDate)
            .ThenBy(b => b.CreatedAt)
            .ThenBy(b => b.Id);

        foreach (var batch in candidates)
        {
            if (remaining == 0)
            {
                break;
            }

            var available = (batch.Quantity ?? 0) + heldByOrder.GetValueOrDefault(batch.Id);
            if (available <= 0)
            {
                continue;
            }

            var take = Math.Min(available, remaining);
            batches.Add(
                new BatchAllocationSuggestionDto(
                    batch.Id,
                    batch.Lot,
                    batch.ExpirationDate,
                    batch.Location,
                    available,
                    take
                )
            );
            remaining -= take;
        }

        return new ItemAllocationSuggestionDto(
            product.Id,
            product.SKU,
            product.Name,
            quantity,
            quantity - remaining,
            remaining > 0,
            remaining,
            batches
        );
    }
}
