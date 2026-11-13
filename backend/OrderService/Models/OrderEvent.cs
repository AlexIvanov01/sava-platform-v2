namespace OrderService.Models;

// Raised on an order (Order.Raise) and turned into an outbox message by OrderContext when the
// order is saved. They carry no data: the message is built from the order's state at save time.
public abstract record OrderEvent(string Name);

// The order's current items are what is sold to it (on create, update and reopen).
public sealed record OrderPublished() : OrderEvent("Order_Published");

// Nothing is sold to the order any more (on delete and cancel).
public sealed record OrderDeleted() : OrderEvent("Order_Deleted");
