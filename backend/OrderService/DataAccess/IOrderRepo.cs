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
