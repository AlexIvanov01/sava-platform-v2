using System;
using OrderService.Models;

namespace OrderService.Ordering;

// Allowed status changes (POST /api/Orders/{id}/status):
//   InProcess <-> Pending
//   InProcess | Pending -> Completed   (creates the invoice)
//   any -> Cancelled                   (releases the stock; an invoice stays)
//   Cancelled -> Pending               (only without an invoice; takes the stock again)
// Deleting the invoice of a Completed order moves it back to Pending.
public static class OrderLifecycle
{
    public static bool IsEditable(this Order order) =>
        order.Status is OrderStatus.InProcess or OrderStatus.Pending;

    // Null when the change is allowed, otherwise why it isn't.
    public static string? CheckTransition(Order order, OrderStatus target) =>
        (order.Status, target) switch
        {
            (OrderStatus.InProcess, OrderStatus.Pending)
            or (OrderStatus.Pending, OrderStatus.InProcess) => null,
            (OrderStatus.InProcess or OrderStatus.Pending, OrderStatus.Completed) => order.Invoice
            == null
                ? null
                : "The order already has an invoice.",
            (not OrderStatus.Cancelled, OrderStatus.Cancelled) => null,
            (OrderStatus.Cancelled, OrderStatus.Pending) => order.Invoice == null
                ? null
                : "A cancelled order with an invoice can't be reopened; delete the invoice first.",
            _ => $"An order can't go from {order.Status} to {target}.",
        };

    // Makes an allowed change (see CheckTransition) and raises the event it implies: cancelling
    // releases the stock (OrderDeleted), reopening a cancelled order takes it again
    // (OrderPublished). Completing creates no event; the caller adds the invoice.
    public static void Apply(Order order, OrderStatus target, DateTime now)
    {
        var previous = order.Status;
        switch (target)
        {
            case OrderStatus.Completed:
                order.CompletedDate = now;
                break;
            case OrderStatus.Cancelled:
                order.CancelledDate = now;
                order.Raise(new OrderDeleted());
                break;
            case OrderStatus.Pending when previous == OrderStatus.Cancelled:
                order.CancelledDate = null;
                order.Raise(new OrderPublished());
                break;
        }
        order.Status = target;
    }
}
