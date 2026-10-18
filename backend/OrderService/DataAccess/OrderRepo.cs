using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OrderService.Models;
using Serilog;

namespace OrderService.DataAccess;

public class OrderRepo : IOrderRepo
{
    private readonly OrderContext _context;

    public OrderRepo(OrderContext orderContext)
    {
        _context = orderContext;
    }

    public async Task<Order?> CreateOrderAsync(Order order)
    {
        if (order.OrderItems.Count == 0) 
        {
            Log.Error("Invalid order: Order has no items.");
            return null;
        }

        var dbCustomer = await _context.Customers
            .AsNoTracking()
            .SingleOrDefaultAsync(c => c.ExternalId == order.CustomerId);

        if (dbCustomer == null)
        {
            Log.Error("Invalid order: Customer with id {Id} was not found", order.CustomerId);
            return null;
        }

        var itemIdList = new List<Guid>();

        foreach (var item in order.OrderItems)
        {
            itemIdList.Add(item.ItemId);
        }

        var validItems = await _context.Items
            .Where(i => itemIdList.Contains(i.ExternalBatchId))
            .ToListAsync();

        if (itemIdList.Count != validItems.Count)
        {
            Log.Error("Invalid Order: Order contains invalid items.");
            return null;
        }

        order.Id = Guid.NewGuid();

        foreach (var item in order.OrderItems)
        {
            item.Order = order;
            item.OrderId = order.Id;
            item.Item = validItems.Single(i => i.ExternalBatchId == item.ItemId);
        }

        _context.Orders.Add(order);

        await _context.SaveChangesAsync();

        order.Customer = dbCustomer;

        return order;
    }

    public async Task<Order?> DeleteOrderAsync(Guid id)
    {
        var dbOrder = await _context.Orders
            .AsNoTracking()
            .Include(o => o.OrderItems)
            .SingleOrDefaultAsync(x => x.Id == id);

        if (dbOrder == null)
        {
            return null;
        }

        await _context.OrderItems
            .Where(oi => oi.OrderId == id)
            .ExecuteDeleteAsync();

        await _context.Orders
            .Where(o => o.Id == id)
            .ExecuteDeleteAsync();

        return dbOrder;
    }

    public async Task<int> GetOrderCountAsync()
    {
        return await _context.Orders.CountAsync();
    }

    public async Task<IEnumerable<Order>> GetAllOrdersAsync(Guid? cursor, int pageSize)
    {
        if (cursor != null)
        {
            return await _context.Orders
                .AsNoTracking()
                .Where(o => o.Id > cursor)
                .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Item)
                .Include(o => o.Invoice)
                .Include(o => o.Customer)
                .OrderBy(o => o.Id)
                .Take(pageSize)
                .ToArrayAsync();
        }
        return await _context.Orders
            .AsNoTracking()
            .Include(o => o.Invoice)
            .Include(o => o.Customer)
            .Include(o => o.OrderItems)
            .ThenInclude(oi => oi.Item)
            .OrderBy(o => o.Id)
            .Take(pageSize)
            .ToArrayAsync();
    }

    public async Task<IEnumerable<Order>> GetAllOrdersByCustomerIdAsync 
        (Guid? customerId, Guid? cursor, int pageSize)
    {
        if (cursor != null)
        {
            return await _context.Orders
                .AsNoTracking()
                .Where(o => o.CustomerId == customerId)
                .Where(o => o.Id > cursor)
                .Include(o => o.OrderItems)
                .ThenInclude(oi => oi.Item)
                .Include(o => o.Customer)
                .Include(o => o.Invoice)
                .OrderBy(o => o.Id)
                .Take(pageSize)
                .ToListAsync();           
        }
        return await _context.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId)
            .Include(o => o.OrderItems)
            .ThenInclude(oi => oi.Item)
            .Include(o => o.Customer)
            .Include(o => o.Invoice)
            .OrderBy(o => o.Id)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<Order?> GetOrderByIdAsync(Guid id)
    {
        var order = await _context.Orders
            .AsNoTracking()
            .Where(p => p.Id == id)
            .Include(o => o.OrderItems)
            .ThenInclude(oi => oi.Item)
            .Include(o => o.Customer)
            .Include(o => o.Invoice)
            .SingleOrDefaultAsync();

        return order;
    }

    public async Task<(Order Order, List<OrderItem> PreviousItems)?> UpdateOrderAsync(Order order)
    {
        var dbOrder = await _context.Orders
            .Include(o => o.OrderItems)
            .Include(o => o.Invoice)
            .SingleOrDefaultAsync(p => p.Id == order.Id);

        if (dbOrder == null)
        {
            Log.Error("--> Order with id {Id} not found for updating.", order.Id);
            return null;
        }

        if (order.CustomerId != null)
        {
            var customerExists = await _context.Customers
                .AnyAsync(c => c.ExternalId == order.CustomerId);

            if (!customerExists)
            {
                Log.Error("Could not update order: Customer with id {Id} was not found", order.CustomerId);
                return null;
            }
        }

        var previousItems = dbOrder.OrderItems
            .Select(oi => new OrderItem { OrderId = oi.OrderId, ItemId = oi.ItemId, OrderItemQuantity = oi.OrderItemQuantity })
            .ToList();

        // Null fields in the update are left unchanged.
        dbOrder.OrderDate = order.OrderDate ?? dbOrder.OrderDate;
        dbOrder.ShippedDate = order.ShippedDate ?? dbOrder.ShippedDate;
        dbOrder.ShippingAddress = order.ShippingAddress ?? dbOrder.ShippingAddress;
        dbOrder.OrderStatus = order.OrderStatus ?? dbOrder.OrderStatus;
        dbOrder.CustomerId = order.CustomerId ?? dbOrder.CustomerId;

        // A non-empty item list replaces the order's items; an empty one leaves them unchanged.
        if (order.OrderItems.Count != 0)
        {
            var itemIdList = order.OrderItems.Select(oi => oi.ItemId).ToList();

            var validItems = await _context.Items
                .Where(i => itemIdList.Contains(i.ExternalBatchId))
                .ToListAsync();

            if (itemIdList.Count != validItems.Count)
            {
                Log.Error("Could not update order: Order contains invalid items.");
                return null;
            }

            var newQuantities = order.OrderItems.ToDictionary(oi => oi.ItemId, oi => oi.OrderItemQuantity);

            // Merge instead of clear-and-add: re-adding an item with the same (OrderId, ItemId) key would conflict with the tracked one.
            dbOrder.OrderItems.RemoveAll(oi => !newQuantities.ContainsKey(oi.ItemId));

            foreach (var (itemId, quantity) in newQuantities)
            {
                var existing = dbOrder.OrderItems.SingleOrDefault(oi => oi.ItemId == itemId);
                if (existing != null)
                {
                    existing.OrderItemQuantity = quantity;
                }
                else
                {
                    dbOrder.OrderItems.Add(new OrderItem
                    {
                        Order = dbOrder,
                        OrderId = dbOrder.Id,
                        Item = validItems.Single(i => i.ExternalBatchId == itemId),
                        ItemId = itemId,
                        OrderItemQuantity = quantity
                    });
                }
            }
        }

        await _context.SaveChangesAsync();

        return (dbOrder, previousItems);
    }
}
