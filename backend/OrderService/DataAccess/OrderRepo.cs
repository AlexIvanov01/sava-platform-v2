using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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

    public async Task<int> GetOrderCountAsync()
    {
        return await _context.Orders.CountAsync();
    }

    public async Task<IEnumerable<Order>> GetAllOrdersAsync(Guid? cursor, int pageSize) =>
        await Page(_context.Orders, cursor, pageSize);

    public async Task<IEnumerable<Order>> GetAllOrdersByCustomerIdAsync(
        Guid? customerId,
        Guid? cursor,
        int pageSize
    ) => await Page(_context.Orders.Where(o => o.CustomerId == customerId), cursor, pageSize);

    public async Task<Order?> GetOrderByIdAsync(Guid id) =>
        await WithDetails(_context.Orders.AsNoTracking()).SingleOrDefaultAsync(o => o.Id == id);

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
