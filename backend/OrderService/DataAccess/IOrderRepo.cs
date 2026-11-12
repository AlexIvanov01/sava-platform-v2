using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OrderService.Models;

namespace OrderService.DataAccess;

public interface IOrderRepo
{
    Task<IEnumerable<Order>> GetAllOrdersAsync(Guid? cursor, int pageSize);
    Task<int> GetOrderCountAsync();
    Task<Order?> GetOrderByIdAsync(Guid id);
    Task<IEnumerable<Order>> GetAllOrdersByCustomerIdAsync(
        Guid? customerId,
        Guid? cursor,
        int pageSize
    );

    // Assigns the order a new Id and saves it with its items.
    Task CreateOrderAsync(Order order);

    // Tracked, with items, batches and invoice; change it, then call SaveChangesAsync.
    Task<Order?> GetOrderForUpdateAsync(Guid id);
    Task SaveChangesAsync();
    Task<(DeleteOrderStatus Status, Order? Order)> DeleteOrderAsync(Guid id);
}

public enum DeleteOrderStatus
{
    NotFound,
    Deleted,
    HasInvoice,
}
