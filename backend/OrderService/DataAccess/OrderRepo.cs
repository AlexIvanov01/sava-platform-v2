using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OrderService.Dtos;
using OrderService.Models;

namespace OrderService.DataAccess;

public class OrderRepo : IOrderRepo
{
    private readonly OrderContext _context;

    public OrderRepo(OrderContext orderContext)
    {
        _context = orderContext;
    }

    public async Task CreateOrderAsync(Order order)
    {
        order.Id = Guid.NewGuid();
        _context.Orders.Add(order);
        await _context.SaveChangesAsync();
    }

    public async Task<Order?> GetOrderForUpdateAsync(Guid id) =>
        await _context
            .Orders.Include(o => o.Items)
                .ThenInclude(l => l.Batches)
            .Include(o => o.Invoice)
            .AsSplitQuery()
            .SingleOrDefaultAsync(o => o.Id == id);

    public async Task SaveChangesAsync() => await _context.SaveChangesAsync();

    public async Task<(DeleteOrderStatus Status, Order? Order)> DeleteOrderAsync(Guid id)
    {
        var dbOrder = await WithDetails(_context.Orders.AsNoTracking())
            .SingleOrDefaultAsync(x => x.Id == id);

        if (dbOrder == null)
        {
            return (DeleteOrderStatus.NotFound, null);
        }

        if (dbOrder.Invoice != null)
        {
            return (DeleteOrderStatus.HasInvoice, dbOrder);
        }

        // A single statement keeps the delete atomic; the database cascades it to the items.
        await _context.Orders.Where(o => o.Id == id).ExecuteDeleteAsync();

        return (DeleteOrderStatus.Deleted, dbOrder);
    }

    public async Task<int> GetOrderCountAsync(OrderFilter filter) =>
        await Filtered(filter).CountAsync();

    public async Task<IEnumerable<Order>> GetOrdersAsync(
        OrderFilter filter,
        Guid? cursor,
        int pageSize
    ) => await Page(Filtered(filter), cursor, pageSize);

    public async Task<Order?> GetOrderByIdAsync(Guid id) =>
        await WithDetails(_context.Orders.AsNoTracking()).SingleOrDefaultAsync(o => o.Id == id);

    public async Task<List<Order>> GetCompletedOrdersAsync(
        DateOnly? from,
        DateOnly? to,
        Guid? customerId
    )
    {
        var orders = _context.Orders.Where(o =>
            o.Status == OrderStatus.Completed && o.Invoice != null
        );
        if (customerId != null)
        {
            orders = orders.Where(o => o.CustomerId == customerId);
        }
        if (from is { } start)
        {
            var startTime = start.ToDateTime(TimeOnly.MinValue);
            orders = orders.Where(o => o.Invoice!.InvoiceDate >= startTime);
        }
        if (to is { } end)
        {
            var endTime = end.AddDays(1).ToDateTime(TimeOnly.MinValue);
            orders = orders.Where(o => o.Invoice!.InvoiceDate < endTime);
        }
        return await WithDetails(orders.AsNoTracking()).ToListAsync();
    }

    private IQueryable<Order> Filtered(OrderFilter filter)
    {
        var orders = _context.Orders.AsQueryable();
        if (filter.Status is { } status)
        {
            orders = orders.Where(o => o.Status == status);
        }
        if (filter.CustomerId is { } customerId)
        {
            orders = orders.Where(o => o.CustomerId == customerId);
        }
        if (filter.From is { } from)
        {
            var start = from.ToDateTime(TimeOnly.MinValue);
            orders = orders.Where(o => o.OrderDate >= start);
        }
        if (filter.To is { } to)
        {
            var end = to.AddDays(1).ToDateTime(TimeOnly.MinValue);
            orders = orders.Where(o => o.OrderDate < end);
        }
        return orders;
    }

    private static async Task<List<Order>> Page(
        IQueryable<Order> orders,
        Guid? cursor,
        int pageSize
    )
    {
        if (cursor != null)
        {
            orders = orders.Where(o => o.Id > cursor);
        }
        return await WithDetails(orders.AsNoTracking())
            .OrderBy(o => o.Id)
            .Take(pageSize)
            .ToListAsync();
    }

    private static IQueryable<Order> WithDetails(IQueryable<Order> orders) =>
        orders
            .Include(o => o.Items)
                .ThenInclude(l => l.Batches)
            .Include(o => o.Invoice)
            .AsSplitQuery();
}
