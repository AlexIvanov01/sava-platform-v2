using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OrderService.Dtos;
using OrderService.Models;

namespace OrderService.DataAccess;

public interface IInvoiceRepo
{
    // today decides InvoiceFilter.Overdue.
    Task<IEnumerable<Invoice>> GetInvoicesAsync(
        InvoiceFilter filter,
        DateOnly today,
        Guid? cursor,
        int pageSize
    );
    Task<int> GetInvoiceCountAsync(InvoiceFilter filter, DateOnly today);

    // With its order (for the order's status).
    Task<Invoice?> GetInvoiceByIdAsync(Guid id);

    // Tracked, with its order; change it, then call SaveChangesAsync.
    Task<Invoice?> GetInvoiceForUpdateAsync(Guid id);
    Task SaveChangesAsync();

    // Also moves a Completed order back to Pending; null when the invoice doesn't exist.
    Task<Invoice?> DeleteInvoiceAsync(Guid id);

    Task<bool> NumberExistsAsync(string number);

    // One more than the highest number in use.
    Task<string> NextNumberAsync();
}
