using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CustomerService.Dtos;
using CustomerService.Models;

namespace CustomerService.DataAccess;

public interface ICustomerRepo
{
    // Every customer, for the gRPC sync to OrderService.

    // Keyset page ordered by Id, each customer with whether any order references it.
    Task<IEnumerable<(Customer Customer, bool HasOrders)>> GetCustomersAsync(
        CustomerFilter filter,
        Guid? cursor,
        int pageSize
    );
    Task<int> GetCustomersCountAsync(CustomerFilter filter);
    Task<Customer?> GetCustomerAsync(Guid id);
    Task<bool> HasOrdersAsync(Guid customerId);
    Task CreateCustomerAsync(Customer customer);
    Task<(DeleteCustomerStatus Status, Customer? Customer)> DeleteCustomerAsync(Guid id);
    Task<Customer?> UpdateCustomerAsync(Customer customer);

    // Order references, maintained from OrderService's order events.
    Task SetOrderCustomerAsync(Guid orderId, Guid customerId);
    Task RemoveOrderAsync(Guid orderId);
}

public enum DeleteCustomerStatus
{
    NotFound,
    Deleted,
    HasOrders,
}
