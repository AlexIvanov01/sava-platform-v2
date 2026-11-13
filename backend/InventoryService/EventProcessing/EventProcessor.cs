using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using InventoryService.DataAccess;
using InventoryService.Dtos;
using InventoryService.Messaging;
using Serilog;

namespace InventoryService.EventProcessing;

// Scoped: InboxHandler resolves it in the scope (and DbContext transaction) that records the
// inbox message, so the stock change and the record are committed together.
public class EventProcessor(IStockRepo stock) : IEventProcessor
{
    public async Task ProcessEvent(string message)
    {
        var dto = JsonSerializer.Deserialize<OrderPublishedDto>(message);
        switch (dto?.Event)
        {
            case InboxHandler.OrderPublished:
                await SetOrderStock(dto, deleted: false);
                break;
            case InboxHandler.OrderDeleted:
                await SetOrderStock(dto, deleted: true);
                break;
            default:
                throw new InvalidEventException($"Unknown event '{dto?.Event}'.");
        }
    }

    // Order_Published carries the order's full batch quantities, so it becomes the target of what is sold
    // to the order; Order_Deleted sets that target to nothing. The ledger posts only the difference,
    // which makes redelivered or repeated messages harmless.
    private async Task SetOrderStock(OrderPublishedDto dto, bool deleted)
    {
        if (dto.OrderId is not { } orderId || orderId == Guid.Empty)
        {
            throw new InvalidEventException("The message has no OrderId.");
        }
        var batches = (dto.Items ?? []).SelectMany(l => l.Batches ?? []).ToList();
        // An order always has items, so an empty list is a broken message, not "sell nothing".
        if (!deleted && batches.Count == 0)
        {
            throw new InvalidEventException("Order_Published has no items.");
        }

        if (deleted && !await stock.OrderHasMovementsAsync(orderId))
        {
            // E.g. orders from the development seed, which never published their stock.
            Log.Warning(
                "--> Order {OrderId} has no stock movements; there is nothing to return to stock.",
                orderId
            );
            return;
        }

        var target = deleted
            ? []
            : batches.GroupBy(b => b.BatchId).ToDictionary(g => g.Key, g => g.Sum(b => b.Quantity));

        var unknown = await stock.SetOrderStockAsync(orderId, target);

        if (unknown.Count > 0)
        {
            Log.Warning(
                "--> Order {OrderId} references unknown batches {BatchIds}; they were skipped.",
                orderId,
                unknown
            );
        }
        Log.Information(
            "--> Stock for order {OrderId} set to {Target}.",
            orderId,
            deleted
                ? "nothing (order deleted)"
                : string.Join(", ", target.Select(t => $"{t.Key}: {t.Value}"))
        );
    }
}
