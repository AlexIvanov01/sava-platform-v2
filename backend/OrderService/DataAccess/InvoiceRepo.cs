using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OrderService.Dtos;
using OrderService.Invoicing;
using OrderService.Models;

namespace OrderService.DataAccess;

public class InvoiceRepo : IInvoiceRepo
{
    private readonly OrderContext _context;

    public InvoiceRepo(OrderContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<Invoice>> GetInvoicesAsync(
        InvoiceFilter filter,
        DateOnly today,
        Guid? cursor,
        int pageSize
    )
    {
        var invoices = Filtered(filter, today);
        if (cursor != null)
        {
            invoices = invoices.Where(i => i.Id > cursor);
        }
        return await invoices
            .AsNoTracking()
            .Include(i => i.Order)
            .OrderBy(i => i.Id)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<int> GetInvoiceCountAsync(InvoiceFilter filter, DateOnly today) =>
        await Filtered(filter, today).CountAsync();

    public async Task<Invoice?> GetInvoiceByIdAsync(Guid id) =>
        await _context
            .Invoices.AsNoTracking()
            .Include(i => i.Order)
            .SingleOrDefaultAsync(i => i.Id == id);

    public async Task<Invoice?> GetInvoiceForUpdateAsync(Guid id) =>
        await _context.Invoices.Include(i => i.Order).SingleOrDefaultAsync(i => i.Id == id);

    public async Task SaveChangesAsync() => await _context.SaveChangesAsync();

    public async Task<Invoice?> DeleteInvoiceAsync(Guid id)
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
        var order = dbInvoice.Order!;
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

    public async Task<bool> NumberExistsAsync(string number) =>
        await _context.Invoices.AnyAsync(i => i.Number == number);

    public async Task<string> NextNumberAsync() =>
        InvoiceFactory.NextNumber(await _context.Invoices.MaxAsync(i => i.Number));

    private IQueryable<Invoice> Filtered(InvoiceFilter filter, DateOnly today)
    {
        var invoices = _context.Invoices.AsQueryable();
        if (filter.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue);
            invoices = invoices.Where(i => i.InvoiceDate >= start);
        }
        if (filter.To is { } to)
        {
            var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            invoices = invoices.Where(i => i.InvoiceDate < end);
        }
        if (filter.CustomerId is { } customerId)
        {
            invoices = invoices.Where(i => i.Order!.CustomerId == customerId);
        }
        if (!string.IsNullOrEmpty(filter.Number))
        {
            invoices = invoices.Where(i => i.Number!.Contains(filter.Number));
        }
        if (filter.PaymentStatus is { } status)
        {
            invoices = invoices.Where(i => i.PaymentStatus == status);
        }
        if (filter.Overdue is { } overdue)
        {
            var startOfToday = today.ToDateTime(TimeOnly.MinValue);
            invoices = overdue
                ? invoices.Where(i =>
                    i.PaymentStatus == PaymentStatus.Unpaid && i.DueDate < startOfToday
                )
                : invoices.Where(i =>
                    i.PaymentStatus != PaymentStatus.Unpaid
                    || i.DueDate == null
                    || i.DueDate >= startOfToday
                );
        }
        return invoices;
    }
}
