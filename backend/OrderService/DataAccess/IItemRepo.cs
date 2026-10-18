using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OrderService.Models;

namespace OrderService.DataAccess;

public interface IItemRepo
{
    Task<bool> ExternalItemExistsAsync(Guid? externalId);
    Task АddItemAsync(Item item);
    Task DeleteItemAsync(Guid? id);
    Task UpdateItemAsync(Item item);
    Task UpdateProductNameAsync(Guid? productId, string newProductName);
    Task DeleteItemsByProductIdAsync(Guid? productId);
    Task SyncItemsAsync(IEnumerable<Item> items);
}
