using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OrderService.Dtos;
using OrderService.Models;

namespace OrderService.DataAccess;

public interface IOrderRepo
{
    Task<IEnumerable<Order>> GetOrdersAsync(OrderFilter filter, Guid? cursor, int pageSize);
    Task<int> GetOrderCountAsync(OrderFilter filter);
    Task<Order?> GetOrderByIdAsync(Guid id);

    // For the sales report: Completed orders whose invoice date falls in [from, to] (inclusive
    // days), with their items and invoice.
    Task<List<Order>> GetCompletedOrdersAsync(DateOnly? from, DateOnly? to, Guid? customerId);

    // Assigns the order a new Id and saves it with its items.
    Task CreateOrderAsync(Order order);

    // Tracked, with items, batches and invoice; change it, then call SaveChangesAsync.
    Task<Order?> GetOrderForUpdateAsync(Guid id);
    Task SaveChangesAsync();

    // Refused (HasInvoice) while the order has an invoice.
    Task<(DeleteOrderStatus Status, Order? Order)> DeleteOrderAsync(Guid id);
}

public enum DeleteOrderStatus
{
    NotFound,
    Deleted,
    HasInvoice,
}
