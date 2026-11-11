using System;
using System.Collections.Generic;

namespace OrderService.Dtos;

// Inventory treats Order_Published as the order's complete item list and Order_Deleted as
// "nothing sold"; it reconciles its stock ledger per (OrderId, batch).
public class OrderPublishedEvent
{
    public Guid OrderId { get; set; }
    public IEnumerable<OrderItemPublishedDto> OrderItems { get; set; } = [];
    public string Event { get; set; } = string.Empty;
}
