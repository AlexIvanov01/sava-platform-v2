using System;
using System.Collections.Generic;

namespace OrderService.Dtos;

// Both events describe the order's current state. Inventory treats Order_Published as the
// order's complete list of batch quantities and Order_Deleted as "nothing sold", reconciling its
// stock ledger per (OrderId, batch); CustomerService records which customer the order belongs to.
// MessageId is the outbox message's Id, which the subscribers' inboxes use to drop duplicates.
public class OrderPublishedEvent
{
    public Guid MessageId { get; set; }
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public IEnumerable<OrderItemPublishedDto> Items { get; set; } = [];
    public string Event { get; set; } = string.Empty;
}

public record OrderItemPublishedDto(
    Guid ProductId,
    IEnumerable<OrderItemBatchPublishedDto> Batches
);

public record OrderItemBatchPublishedDto(Guid BatchId, int Quantity);
