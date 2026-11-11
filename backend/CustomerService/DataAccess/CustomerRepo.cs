using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CustomerService.Dtos;
using CustomerService.Models;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.DataAccess
{
    public class CustomerRepo : ICustomerRepo
    {
        private readonly CustomerContext _context;

        public CustomerRepo(CustomerContext context)
        {
            _context = context;
        }

        public async Task CreateCustomerAsync(Customer customer)
        {
            await _context.Customers.AddAsync(customer);
            await _context.SaveChangesAsync();
        }

        public async Task<(DeleteCustomerStatus Status, Customer? Customer)> DeleteCustomerAsync(
            Guid id
        )
        {
            var dbCustomer = await _context
                .Customers.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dbCustomer == null)
            {
                return (DeleteCustomerStatus.NotFound, null);
            }

            if (await HasOrdersAsync(id))
            {
                return (DeleteCustomerStatus.HasOrders, dbCustomer);
            }

            _context.Customers.Attach(dbCustomer);
            _context.Customers.Remove(dbCustomer);

            await _context.SaveChangesAsync();

            return (DeleteCustomerStatus.Deleted, dbCustomer);
        }

        public async Task<IEnumerable<(Customer Customer, bool HasOrders)>> GetCustomersAsync(
            CustomerFilter filter,
            Guid? cursor,
            int pageSize
        )
        {
            var query = Filter(_context.Customers.AsNoTracking(), filter);

            if (cursor != null)
            {
                query = query.Where(c => c.Id > cursor);
            }

            var rows = await query
                .OrderBy(c => c.Id)
                .Take(pageSize)
                .Select(c => new
                {
                    Customer = c,
                    HasOrders = _context.CustomerOrders.Any(o => o.CustomerId == c.Id),
                })
                .ToListAsync();

            return rows.Select(r => (r.Customer, r.HasOrders));
        }

        public async Task<int> GetCustomersCountAsync(CustomerFilter filter) =>
            await Filter(_context.Customers, filter).CountAsync();

        // Substring matches (case-insensitive under MySQL's default collation); Name matches
        // either the contact name or the company name.
        private static IQueryable<Customer> Filter(
            IQueryable<Customer> query,
            CustomerFilter filter
        )
        {
            if (!string.IsNullOrWhiteSpace(filter.Name))
            {
                query = query.Where(c =>
                    c.Name!.Contains(filter.Name) || c.CompanyName!.Contains(filter.Name)
                );
            }
            if (!string.IsNullOrWhiteSpace(filter.UIC))
            {
                query = query.Where(c => c.UIC!.Contains(filter.UIC));
            }
            if (!string.IsNullOrWhiteSpace(filter.VATNumber))
            {
                query = query.Where(c => c.VATNumber!.Contains(filter.VATNumber));
            }
            return query;
        }

        public async Task<bool> HasOrdersAsync(Guid customerId) =>
            await _context.CustomerOrders.AnyAsync(o => o.CustomerId == customerId);

        public async Task SetOrderCustomerAsync(Guid orderId, Guid customerId)
        {
            var reference = await _context.CustomerOrders.SingleOrDefaultAsync(o =>
                o.OrderId == orderId
            );

            if (reference == null)
            {
                _context.CustomerOrders.Add(
                    new CustomerOrder { OrderId = orderId, CustomerId = customerId }
                );
            }
            else
            {
                reference.CustomerId = customerId;
            }

            await _context.SaveChangesAsync();
        }

        public Task RemoveOrderAsync(Guid orderId) =>
            _context.CustomerOrders.Where(o => o.OrderId == orderId).ExecuteDeleteAsync();

        public async Task<IEnumerable<Customer>> GetAllCustomersAsync()
        {
            return await _context.Customers.AsNoTracking().ToListAsync();
        }

        public async Task<Customer?> GetCustomerAsync(Guid id)
        {
            var customer = await _context
                .Customers.AsNoTracking()
                .SingleOrDefaultAsync(p => p.Id == id);

            return customer;
        }

        public async Task<Customer?> UpdateCustomerAsync(Customer customer)
        {
            var dbCustomer = await _context.Customers.SingleOrDefaultAsync(p =>
                p.Id == customer.Id
            );

            if (dbCustomer == null)
            {
                return null;
            }

            // Null fields in the update are left unchanged.
            dbCustomer.Name = customer.Name ?? dbCustomer.Name;
            dbCustomer.CompanyName = customer.CompanyName ?? dbCustomer.CompanyName;
            dbCustomer.Email = customer.Email ?? dbCustomer.Email;
            dbCustomer.PhoneNumber = customer.PhoneNumber ?? dbCustomer.PhoneNumber;
            dbCustomer.Address = customer.Address ?? dbCustomer.Address;
            dbCustomer.City = customer.City ?? dbCustomer.City;
            dbCustomer.Country = customer.Country ?? dbCustomer.Country;
            dbCustomer.BankName = customer.BankName ?? dbCustomer.BankName;
            dbCustomer.IBAN = customer.IBAN ?? dbCustomer.IBAN;
            dbCustomer.BIC = customer.BIC ?? dbCustomer.BIC;
            dbCustomer.VATNumber = customer.VATNumber ?? dbCustomer.VATNumber;
            dbCustomer.UIC = customer.UIC ?? dbCustomer.UIC;

            await _context.SaveChangesAsync();

            return dbCustomer;
        }
    }
}
