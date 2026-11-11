using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using InventoryService.Models;

namespace InventoryService.DataAccess;

public interface ICategoryRepo
{
    // Ordered by name; each category comes with the number of products in it.
    Task<IEnumerable<(Category Category, int ProductCount)>> GetAllCategoriesAsync();
    Task<(Category Category, int ProductCount)?> GetCategoryAsync(Guid id);
    Task<bool> CategoryExistsAsync(Guid id);
    Task<bool> NameExistsAsync(string name, Guid? exceptCategoryId = null);
    Task CreateCategoryAsync(Category category);
    Task<Category?> UpdateCategoryAsync(Category category);
    Task<DeleteCategoryStatus> DeleteCategoryAsync(Guid id);
}

public enum DeleteCategoryStatus
{
    NotFound,
    Deleted,
    HasProducts,
}
