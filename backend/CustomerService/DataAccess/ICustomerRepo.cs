using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CustomerService.Models;

namespace CustomerService.DataAccess;

public interface ICustomerRepo
{
    Task<IEnumerable<Customer>> GetAllCustomersAsync();
    Task<Customer?> GetCustomerAsync(Guid id);
    Task CreateCustomerAsync(Customer customer);
    Task<Customer?> DeleteCustomerAsync(Guid id);
    Task<Customer?> UpdateCustomerAsync(Customer customer);
}
