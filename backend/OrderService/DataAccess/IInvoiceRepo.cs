using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OrderService.Models;

namespace OrderService.DataAccess;

public interface IInvoiceRepo
{
    Task<IEnumerable<Invoice>> GetAllInvoicesAsync(int? cursor, int pageSize);
    Task<Invoice?> GetInvoiceByIdAsync(int id);
    Task<Invoice?> GetInvoiceByOrderIdAsync(Guid orderId);
    Task<Invoice?> UpdateInvoiceAsync(Invoice invoice);

    // Also moves a Completed order back to Pending; null when the invoice doesn't exist.
    Task<Invoice?> DeleteInvoiceAsync(int id);
    Task<int> GetInvoiceCountAsync();
}
