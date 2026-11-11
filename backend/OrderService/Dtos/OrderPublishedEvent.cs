using System;
using System.Collections.Generic;

namespace OrderService.Dtos;

// Both events describe the order's current state. Inventory treats Order_Published as the
// order's complete item list and Order_Deleted as "nothing sold", reconciling its stock ledger
// per (OrderId, batch); CustomerService records which customer the order belongs to.
public class OrderPublishedEvent
{
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public IEnumerable<OrderItemPublishedDto> OrderItems { get; set; } = [];
    public string Event { get; set; } = string.Empty;
}
