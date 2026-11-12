using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
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

    // Starts a transaction that holds the invoice numbering lock until it is committed or
    // disposed. Call NextNumberAsync and save the new invoice inside it, so concurrent
    // completions don't take the same number.
    Task<IDbContextTransaction> BeginNumberingAsync();
}
