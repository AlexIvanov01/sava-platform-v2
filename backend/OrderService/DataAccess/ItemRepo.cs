using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OrderService.Models;
using Serilog;

namespace OrderService.DataAccess;

public class ItemRepo : IItemRepo
{
    private readonly OrderContext _context;

    public ItemRepo(OrderContext context)
    {
        _context = context;
    }

    public async Task DeleteItemAsync(Guid? id)
    {
        await _context.Items.Where(i => i.ExternalBatchId == id).ExecuteDeleteAsync();
    }

    public async Task DeleteItemsByProductIdAsync(Guid? productId)
    {
        await _context.Items.Where(i => i.ExternalProductId == productId).ExecuteDeleteAsync();
    }

    public async Task<bool> ExternalItemExistsAsync(Guid? externalId)
    {
        return await _context.Items.AnyAsync(i => i.ExternalBatchId == externalId);
    }

    public async Task SyncItemsAsync(IEnumerable<Item> items)
    {
        var dbItemArray = await _context.Items.AsNoTracking().ToListAsync();

        foreach (var item in items)
        {
            if (!dbItemArray.Exists(i => i.ExternalBatchId == item.ExternalBatchId))
            {
                _context.Items.Add(item);
            }
            else
            {
                _context.Items.Update(item);
            }
        }
        await _context.SaveChangesAsync();

        var syncedIds = items.Select(i => i.ExternalBatchId).ToHashSet();
        var invalidItems = dbItemArray.Where(i => !syncedIds.Contains(i.ExternalBatchId)).ToList();
        if (invalidItems.Count > 0)
        {
            var validatedItems = await _context.OrderItems.Select(io => io.ItemId).ToListAsync();

            foreach (var itemId in validatedItems)
            {
                var itemToBeRemoved = invalidItems.Find(c => c.ExternalBatchId == itemId);
                if (itemToBeRemoved != null)
                {
                    invalidItems.Remove(itemToBeRemoved);
                }
            }

            if (invalidItems.Count > 0)
            {
                await _context.Items.Where(i => invalidItems.Contains(i)).ExecuteDeleteAsync();
            }
        }
    }

    public async Task UpdateItemAsync(Item item)
    {
        var dbItem = await _context
            .Items.AsNoTracking()
            .SingleOrDefaultAsync(p => p.ExternalBatchId == item.ExternalBatchId);

        if (dbItem == null)
        {
            //To-do: Create an appropriate response to an update for an non existent object
            Log.Error(
                "Could not find item with external id {Id} in DB to update.",
                item.ExternalBatchId
            );
            return;
        }

        dbItem = item;
        _context.Entry(dbItem).State = EntityState.Modified;

        foreach (var property in _context.Entry(dbItem).Properties)
        {
            if (property.CurrentValue == null)
            {
                property.IsModified = false;
            }
        }
        _context.Entry(dbItem).Property(p => p.ExternalBatchId).IsModified = false;
        _context.Entry(dbItem).Property(p => p.ExternalBatchId).IsModified = false;
        _context.Entry(dbItem).Property(p => p.ExternalProductId).IsModified = false;

        await _context.SaveChangesAsync();
    }

    public async Task UpdateProductNameAsync(Guid? productId, string newProductName)
    {
        await _context
            .Items.Where(i => i.ExternalProductId == productId)
            .ExecuteUpdateAsync(p => p.SetProperty(i => i.Name, newProductName));
    }

    public async Task AddItemAsync(Item item)
    {
        _context.Items.Add(item);
        await _context.SaveChangesAsync();
    }
}
