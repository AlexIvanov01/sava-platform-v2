using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OrderService.Models;

namespace OrderService.DataAccess;

public interface ICustomerRepo
{
    Task<bool> ExternalCustomerExistsAsync(Guid? externalId);
    Task<Customer?> GetCustomerAsync(Guid id);
    Task AddCustomerAsync(Customer customer);
    Task DeleteCustomerAsync(Guid id);
    Task UpdateCustomerAsync(Customer customer);
    Task SyncCustomersAsync(IEnumerable<Customer> customers);
}
