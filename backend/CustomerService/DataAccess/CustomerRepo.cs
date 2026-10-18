using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        public async Task<Customer?> DeleteCustomerAsync(Guid id)
        {
            var dbCustomer = await _context
                .Customers.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id);

            if (dbCustomer == null)
            {
                return null;
            }

            _context.Customers.Attach(dbCustomer);
            _context.Customers.Remove(dbCustomer);

            await _context.SaveChangesAsync();

            return dbCustomer;
        }

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
