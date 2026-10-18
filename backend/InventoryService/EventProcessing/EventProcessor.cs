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
                await RemoveItems(message);
                break;
            case EventType.OrderDeleted:
                await AddItems(message);
                break;
            default:
                break;
        }
    }

    private async Task AddItems(string orderDeltedMessage)
    {
        using var scope = _scopeFactory.CreateScope();

        var repo = scope.ServiceProvider.GetRequiredService<IProductRepo>();

        var orderPublishedDto = JsonSerializer.Deserialize<OrderPublishedDto>(orderDeltedMessage);

        if (orderPublishedDto == null)
        {
            Log.Error("--> Could not update batches because message was deserialized to null");
            return;
        }
        if (!orderPublishedDto.OrderItems.Any())
        {
            Log.Error(
                "--> Could not update batches because message deserialised with no items: {Dto}",
                orderPublishedDto
            );
            return;
        }

        await repo.IncrementBatchesAsync(orderPublishedDto.OrderItems);
        Log.Information("--> Items inceremented successfully.");
    }

    private async Task RemoveItems(string orderPublishedMessage)
    {
        using var scope = _scopeFactory.CreateScope();

        var repo = scope.ServiceProvider.GetRequiredService<IProductRepo>();

        var orderPublishedDto = JsonSerializer.Deserialize<OrderPublishedDto>(
            orderPublishedMessage
        );

        if (orderPublishedDto == null)
        {
            Log.Error("--> Could not update batches because message was deserialized to null");
            return;
        }
        if (!orderPublishedDto.OrderItems.Any())
        {
            Log.Error(
                "--> Could not update batches because message deserialised with no items: {Dto}",
                orderPublishedDto
            );
            return;
        }

        await repo.DecrementBatchesAsync(orderPublishedDto.OrderItems);
        Log.Information("--> Items decremented successfully.");
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
