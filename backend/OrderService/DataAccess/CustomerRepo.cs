using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OrderService.Models;
using Serilog;

namespace OrderService.DataAccess;

public class CustomerRepo : ICustomerRepo
{
    private readonly OrderContext _context;

    public CustomerRepo(OrderContext context)
    {
        _context = context;
    }
    public async Task АddCustomerAsync(Customer customer)
    {
        if (customer.ExternalId != Guid.Empty)
        {
            await _context.Customers.AddAsync(customer);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<Customer?> GetCustomerAsync(Guid id)
    {
        var customer = await _context.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.ExternalId == id);

        return customer;
    }

    public async Task UpdateCustomerAsync(Customer customer)
    {
        var dbCustomer = await _context.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.ExternalId == customer.ExternalId);

        if (dbCustomer == null)
        {
            //To-do: Create an appropriate respone to an update for an non existent object
            Log.Error("Customer for updating was not found: {Id}", customer.ExternalId);
            return;
        }

        dbCustomer = customer;
        _context.Entry(dbCustomer).State = EntityState.Modified;

        foreach (var property in _context.Entry(dbCustomer).Properties)
        {
            if (property.CurrentValue == null)
            {
                property.IsModified = false;
            }
        }
        _context.Entry(dbCustomer).Property(p => p.ExternalId).IsModified = false;

        await _context.SaveChangesAsync();
    }

    public async Task DeleteCustomerAsync(Guid id)
    {
        await _context.Customers
            .Where(x => x.ExternalId == id).ExecuteDeleteAsync();
    }

    public async Task<bool> ExternalCustomerExistsAsync(Guid? externalId)
    {
        return await _context.Customers.AnyAsync(i => i.ExternalId == externalId);
    }

    public async Task SyncCustomersAsync(IEnumerable<Customer> customers)
    {
        var dbCustomerArray = await _context.Customers
            .AsNoTracking()
            .ToListAsync();

        foreach (var customer in customers)
        {
            if (!dbCustomerArray.Exists(c => c.ExternalId == customer.ExternalId))
            {
                _context.Customers.Add(customer);
            }
            else
            {
                _context.Customers.Update(customer);
            }
        }
        await _context.SaveChangesAsync();

        var syncedIds = customers.Select(c => c.ExternalId).ToHashSet();
        var invalidCustomers = dbCustomerArray.Where(c => !syncedIds.Contains(c.ExternalId)).ToList();
        if (invalidCustomers.Count > 0)
        {
            var validatedCustomers = await _context.Orders
                .Select(o => o.CustomerId)
                .ToListAsync();

            foreach (var customerId in validatedCustomers)
            {
                var customerToBeRemoved = invalidCustomers.Find(c => c.ExternalId == customerId);
                if (customerToBeRemoved != null)
                {
                    invalidCustomers.Remove(customerToBeRemoved);
                }
            }

            if (invalidCustomers.Count > 0)
            {
                await _context.Customers
                .Where(c => invalidCustomers.Contains(c))
                .ExecuteDeleteAsync();
            }
        }
    }
}
