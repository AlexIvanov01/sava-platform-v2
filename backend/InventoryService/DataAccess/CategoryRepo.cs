using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InventoryService.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.DataAccess;

public class CategoryRepo(ProductContext context) : ICategoryRepo
{
    public async Task<IEnumerable<(Category Category, int ProductCount)>> GetAllCategoriesAsync()
    {
        var rows = await context
            .Categories.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new { Category = c, ProductCount = c.Products.Count })
            .ToListAsync();

        return rows.Select(r => (r.Category, r.ProductCount));
    }

    public async Task<(Category Category, int ProductCount)?> GetCategoryAsync(Guid id)
    {
        var row = await context
            .Categories.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { Category = c, ProductCount = c.Products.Count })
            .SingleOrDefaultAsync();

        return row == null ? null : (row.Category, row.ProductCount);
    }

    public async Task<bool> CategoryExistsAsync(Guid id) =>
        await context.Categories.AnyAsync(c => c.Id == id);

    public async Task<bool> NameExistsAsync(string name, Guid? exceptCategoryId = null) =>
        exceptCategoryId == null
            ? await context.Categories.AnyAsync(c => c.Name == name)
            : await context.Categories.AnyAsync(c => c.Name == name && c.Id != exceptCategoryId);

    public async Task CreateCategoryAsync(Category category)
    {
        await context.Categories.AddAsync(category);
        await context.SaveChangesAsync();
    }

    public async Task<Category?> UpdateCategoryAsync(Category category)
    {
        var dbCategory = await context.Categories.SingleOrDefaultAsync(c => c.Id == category.Id);

        if (dbCategory == null)
        {
            return null;
        }

        // Null fields in the update are left unchanged.
        dbCategory.Name = category.Name ?? dbCategory.Name;
        dbCategory.Description = category.Description ?? dbCategory.Description;

        await context.SaveChangesAsync();

        return dbCategory;
    }

    public async Task<DeleteCategoryStatus> DeleteCategoryAsync(Guid id)
    {
        var row = await context
            .Categories.Where(c => c.Id == id)
            .Select(c => new { HasProducts = c.Products.Any() })
            .SingleOrDefaultAsync();

        if (row == null)
        {
            return DeleteCategoryStatus.NotFound;
        }

        if (row.HasProducts)
        {
            return DeleteCategoryStatus.HasProducts;
        }

        await context.Categories.Where(c => c.Id == id).ExecuteDeleteAsync();

        return DeleteCategoryStatus.Deleted;
    }
}
