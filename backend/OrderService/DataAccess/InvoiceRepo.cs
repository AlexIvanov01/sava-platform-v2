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
                .ThenInclude(o => o.OrderItems)
                    .ThenInclude(oi => oi.Item)
            .Include(i => i.Order)
                .ThenInclude(o => o.Customer)
            .SingleOrDefaultAsync();

        return invoice;
    }

    public async Task<Invoice?> GetInvoiceByIdAsync(int id)
    {
        return await _context
            .Invoices.AsNoTracking()
            .Where(i => i.Id == id)
            .Include(i => i.Order)
                .ThenInclude(o => o.Customer)
            .Include(i => i.Order)
                .ThenInclude(o => o.OrderItems)
                    .ThenInclude(oi => oi.Item)
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
                    .ThenInclude(o => o.OrderItems)
                        .ThenInclude(oi => oi.Item)
                .Include(i => i.Order)
                    .ThenInclude(o => o.Customer)
                .OrderBy(i => i.Id)
                .Take(pageSize)
                .ToArrayAsync();
        }
        return await _context
            .Invoices.AsNoTracking()
            .Include(i => i.Order)
                .ThenInclude(o => o.OrderItems)
                    .ThenInclude(oi => oi.Item)
            .Include(i => i.Order)
                .ThenInclude(o => o.Customer)
            .OrderBy(i => i.Id)
            .Take(pageSize)
            .ToArrayAsync();
    }

    public async Task<Invoice?> DeleteInvoiceAsync(int id)
    {
        var dbInvoice = await _context
            .Invoices.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id);

        if (dbInvoice == null)
        {
            return null;
        }

        await _context.Invoices.Where(i => i.Id == id).ExecuteDeleteAsync();

        return dbInvoice;
    }

    public async Task<Invoice?> CreateInvoiceAsync(Invoice invoice, Guid orderId)
    {
        bool invoiceCheck = await _context.Invoices.AnyAsync(i => i.OrderId == orderId);

        if (invoiceCheck)
        {
            Log.Error("Invoice creation validation error: Order already has an invoice");
            return null;
        }

        var order = await _context.Orders.SingleOrDefaultAsync(p => p.Id == orderId);

        if (order == null)
        {
            Log.Error("Invoice creation validation error: Order does not exist");
            return null;
        }

        invoice.Order = order;

        _context.Invoices.Add(invoice);

        await _context.SaveChangesAsync();

        return invoice;
    }

    public async Task<int> GetInvoiceCountAsync()
    {
        return await _context.Invoices.CountAsync();
    }
}
