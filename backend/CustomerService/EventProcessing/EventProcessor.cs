using System;
using System.Text.Json;
using System.Threading.Tasks;
using CustomerService.DataAccess;
using CustomerService.Dtos;
using CustomerService.Messaging;
using Serilog;

namespace CustomerService.EventProcessing;

// Tracks which customer each order belongs to. Both events carry the order's current state,
// so applying one twice leaves the right reference. Scoped: InboxHandler resolves it in the scope
// (and DbContext transaction) that records the inbox message.
public class EventProcessor(ICustomerRepo repo) : IEventProcessor
{
    public const string OrderPublished = InboxHandler.OrderPublished;
    public const string OrderDeleted = InboxHandler.OrderDeleted;

    public async Task ProcessEvent(string message)
    {
        var dto = JsonSerializer.Deserialize<OrderPublishedDto>(message);
        var eventName = dto?.Event;
        if (eventName is not (OrderPublished or OrderDeleted))
        {
            throw new InvalidEventException($"Unknown event '{eventName}'.");
        }

        if (dto?.OrderId is not { } orderId || orderId == Guid.Empty)
        {
            throw new InvalidEventException($"{eventName} has no OrderId.");
        }

        if (eventName == OrderDeleted)
        {
            await repo.RemoveOrderAsync(orderId);
            Log.Information("--> Order {OrderId} no longer references a customer.", orderId);
            return;
        }

        if (dto.CustomerId is not { } customerId || customerId == Guid.Empty)
        {
            throw new InvalidEventException($"{eventName} has no CustomerId.");
        }

        await repo.SetOrderCustomerAsync(orderId, customerId);
        Log.Information(
            "--> Order {OrderId} belongs to customer {CustomerId}.",
            orderId,
            customerId
        );
    }
}
