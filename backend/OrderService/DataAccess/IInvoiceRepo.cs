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
    Task<Invoice?> CreateInvoiceAsync(Invoice invoice, Guid orderId);
    Task<Invoice?> UpdateInvoiceAsync(Invoice invoice);
    Task<Invoice?> DeleteInvoiceAsync(int id);
    Task<int> GetInvoiceCountAsync();
}
