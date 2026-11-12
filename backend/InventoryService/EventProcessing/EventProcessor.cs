using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using InventoryService.DataAccess;
using InventoryService.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace InventoryService.EventProcessing;

public class EventProcessor : IEventProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;

    public EventProcessor(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task ProcessEvent(string message)
    {
        var eventType = DetermineEvent(message);

        switch (eventType)
        {
            case EventType.OrderPublished:
                await SetOrderStock(message, deleted: false);
                break;
            case EventType.OrderDeleted:
                await SetOrderStock(message, deleted: true);
                break;
            default:
                break;
        }
    }

    // Order_Published carries the order's full batch quantities, so it becomes the target of what is sold
    // to the order; Order_Deleted sets that target to nothing. The ledger posts only the difference,
    // which makes redelivered or repeated messages harmless.
    private async Task SetOrderStock(string message, bool deleted)
    {
        var dto = JsonSerializer.Deserialize<OrderPublishedDto>(message);

        if (dto == null)
        {
            Log.Error("--> Could not update stock because message was deserialized to null");
            return;
        }
        if (dto.OrderId is not { } orderId || orderId == Guid.Empty)
        {
            Log.Error("--> Could not update stock because the message has no OrderId: {Dto}", dto);
            return;
        }
        var batches = (dto.Items ?? []).SelectMany(l => l.Batches ?? []).ToList();
        // An order always has items, so an empty list is a broken message, not "sell nothing".
        if (!deleted && batches.Count == 0)
        {
            Log.Error(
                "--> Could not update stock because Order_Published has no items: {Dto}",
                dto
            );
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var stock = scope.ServiceProvider.GetRequiredService<IStockRepo>();

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

    private static EventType DetermineEvent(string notificationMessage)
    {
        Log.Information("--> Message: {Message} \n--> Determining Event...", notificationMessage);

        var eventType = JsonSerializer.Deserialize<GenericEventDto>(notificationMessage);

        if (eventType == null)
        {
            Log.Error("--> Notification message deserialized to null.");
            return EventType.Undetermined;
        }

        switch (eventType.Event)
        {
            case "Order_Published":
                Log.Information("--> Order published event detected");
                return EventType.OrderPublished;
            case "Order_Deleted":
                Log.Information("--> Order deleted event detected");
                return EventType.OrderDeleted;
            default:
                Log.Information("--> Could not determine event type");
                return EventType.Undetermined;
        }
    }
}

enum EventType
{
    OrderPublished,
    OrderDeleted,
    Undetermined,
}
