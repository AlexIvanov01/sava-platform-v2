using System;
using System.Text.Json;
using System.Threading.Tasks;
using CustomerService.DataAccess;
using CustomerService.Dtos;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace CustomerService.EventProcessing;

// Tracks which customer each order belongs to. Both events carry the order's current state,
// so applying one twice, or out of order with an update, leaves the right reference.
public class EventProcessor(IServiceScopeFactory scopeFactory) : IEventProcessor
{
    public const string OrderPublished = "Order_Published";
    public const string OrderDeleted = "Order_Deleted";

    public async Task ProcessEvent(string message)
    {
        var eventName = JsonSerializer.Deserialize<GenericEventDto>(message)?.Event;
        if (eventName is not (OrderPublished or OrderDeleted))
        {
            Log.Information("--> Ignoring event {Event}.", eventName);
            return;
        }

        var dto = JsonSerializer.Deserialize<OrderPublishedDto>(message);
        if (dto?.OrderId is not { } orderId || orderId == Guid.Empty)
        {
            Log.Error("--> Could not process {Event}: the message has no OrderId.", eventName);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepo>();

        if (eventName == OrderDeleted)
        {
            await repo.RemoveOrderAsync(orderId);
            Log.Information("--> Order {OrderId} no longer references a customer.", orderId);
            return;
        }

        if (dto.CustomerId is not { } customerId || customerId == Guid.Empty)
        {
            Log.Error("--> Could not process {Event}: the message has no CustomerId.", eventName);
            return;
        }

        await repo.SetOrderCustomerAsync(orderId, customerId);
        Log.Information(
            "--> Order {OrderId} belongs to customer {CustomerId}.",
            orderId,
            customerId
        );
    }
}
