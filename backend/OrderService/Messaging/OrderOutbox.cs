using System;
using System.Text.Json;
using OrderService.Mappings;
using OrderService.Models;

namespace OrderService.Messaging;

public static class OrderOutbox
{
    // The message for an event raised on the order, built from the order's current state.
    public static OutboxMessage ToOutboxMessage(
        this Order order,
        OrderEvent orderEvent,
        DateTime now
    )
    {
        var id = Guid.NewGuid();
        return new OutboxMessage
        {
            Id = id,
            Type = orderEvent.Name,
            AggregateId = order.Id,
            Payload = JsonSerializer.Serialize(order.ToPublishedEvent(orderEvent.Name, id)),
            Status = OutboxStatus.Pending,
            CreatedAt = now,
        };
    }
}
