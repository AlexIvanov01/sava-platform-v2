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

    // Deletes an order loaded with GetOrderForUpdateAsync, with its items; check for an invoice
    // first, the database refuses to delete an order that has one.
    Task DeleteOrderAsync(Order order);
}
