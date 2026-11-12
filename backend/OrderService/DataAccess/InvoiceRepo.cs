using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OrderService.Models;
using Serilog;

namespace OrderService.DataAccess;

public class InvoiceRepo : IInvoiceRepo
{
    private readonly OrderContext _context;

    public InvoiceRepo(OrderContext context)
    {
        _context = context;
    }

    public async Task<Invoice?> UpdateInvoiceAsync(Invoice invoice)
    {
        var dbInvoice = await _context.Invoices.SingleOrDefaultAsync(p => p.Id == invoice.Id);

        if (dbInvoice == null)
        {
            return null;
        }

        // Null fields in the update are left unchanged; the linked order cannot be changed.
        dbInvoice.InvoiceDate = invoice.InvoiceDate ?? dbInvoice.InvoiceDate;
        dbInvoice.InvoiceStatus = invoice.InvoiceStatus ?? dbInvoice.InvoiceStatus;
        dbInvoice.TotalAmount = invoice.TotalAmount ?? dbInvoice.TotalAmount;

        await _context.SaveChangesAsync();

        return dbInvoice;
    }

    public async Task<Invoice?> GetInvoiceByOrderIdAsync(Guid orderId)
    {
        var invoice = await _context
            .Invoices.AsNoTracking()
            .Where(i => i.OrderId == orderId)
            .Include(i => i.Order)
                .ThenInclude(o => o.Items)
                    .ThenInclude(l => l.Batches)
            .SingleOrDefaultAsync();

        return invoice;
    }

    public async Task<Invoice?> GetInvoiceByIdAsync(int id)
    {
        return await _context
            .Invoices.AsNoTracking()
            .Where(i => i.Id == id)
            .Include(i => i.Order)
                .ThenInclude(o => o.Items)
                    .ThenInclude(l => l.Batches)
            .SingleOrDefaultAsync();
    }

    public async Task<IEnumerable<Invoice>> GetAllInvoicesAsync(int? cursor, int pageSize)
    {
        if (cursor != null)
        {
            return await _context
                .Invoices.AsNoTracking()
                .Where(i => i.Id > cursor)
                .Include(i => i.Order)
                    .ThenInclude(o => o.Items)
                        .ThenInclude(l => l.Batches)
                .OrderBy(i => i.Id)
                .Take(pageSize)
                .ToArrayAsync();
        }
        return await _context
            .Invoices.AsNoTracking()
            .Include(i => i.Order)
                .ThenInclude(o => o.Items)
                    .ThenInclude(l => l.Batches)
            .OrderBy(i => i.Id)
            .Take(pageSize)
            .ToArrayAsync();
    }

    public async Task<Invoice?> DeleteInvoiceAsync(int id)
    {
        var dbInvoice = await _context
            .Invoices.Include(i => i.Order)
            .SingleOrDefaultAsync(x => x.Id == id);

        if (dbInvoice == null)
        {
            return null;
        }

        // A Completed order without its invoice goes back to Pending; a Cancelled one stays
        // Cancelled.
        var order = dbInvoice.Order;
        if (order.Status == OrderStatus.Completed)
        {
            order.Status = OrderStatus.Pending;
            order.CompletedDate = null;
        }
        // Touch the order so AuditStamper stamps it even when only the invoice changed.
        order.UpdatedAt = DateTime.UtcNow;

        _context.Invoices.Remove(dbInvoice);
        await _context.SaveChangesAsync();

        return dbInvoice;
    }

    public async Task<int> GetInvoiceCountAsync()
    {
        return await _context.Invoices.CountAsync();
    }
}
