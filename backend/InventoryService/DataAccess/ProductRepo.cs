using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InventoryService.Dtos;
using InventoryService.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.DataAccess;

public class ProductRepo : IProductRepo
{
    private readonly ProductContext _context;

    public ProductRepo(ProductContext context)
    {
        _context = context;
    }

    public async Task CreateProductAsync(Product product)
    {
        product.ProductDateAdded = DateTime.Now;
        await _context.Products.AddAsync(product);
        await _context.SaveChangesAsync();
    }

    public async Task<Product?> DeleteProductAsync(Guid id)
    {
        var dbProduct = await _context
            .Products.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id);

        if (dbProduct == null)
        {
            return null;
        }

        _context.Products.Attach(dbProduct);
        _context.Products.Remove(dbProduct);

        await _context.SaveChangesAsync();

        return dbProduct;
    }

    public async Task<ProductBatch?> DeleteBatchAsync(Guid id)
    {
        var dbBatch = await _context
            .ProductBatches.AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == id);

        if (dbBatch == null)
        {
            return null;
        }

        _context.ProductBatches.Attach(dbBatch);
        _context.ProductBatches.Remove(dbBatch);

        await _context.SaveChangesAsync();

        return dbBatch;
    }

    public async Task<IEnumerable<Product>> GetAllProductsAsync(Guid? cursor, int pageSize)
    {
        if (cursor != null)
        {
            return await _context
                .Products.AsNoTracking()
                .Include(a => a.Batches)
                .Where(p => p.Id > cursor)
                .OrderBy(p => p.Id)
                .Take(pageSize)
                .ToListAsync();
        }
        return await _context
            .Products.AsNoTracking()
            .Include(a => a.Batches)
            .OrderBy(p => p.Id)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<Product?> GetProductAsync(Guid id)
    {
        var product = await _context
            .Products.AsNoTracking()
            .Include(a => a.Batches)
            .SingleOrDefaultAsync(p => p.Id == id);

        return product;
    }

    public async Task<Product?> UpdateProductAsync(Product product)
    {
        var DbProduct = await _context.Products.SingleOrDefaultAsync(p => p.Id == product.Id);

        if (DbProduct == null)
        {
            return null;
        }

        // Null fields in the update are left unchanged.
        DbProduct.Name = product.Name ?? DbProduct.Name;
        DbProduct.Description = product.Description ?? DbProduct.Description;
        DbProduct.Brand = product.Brand ?? DbProduct.Brand;
        DbProduct.Supplier = product.Supplier ?? DbProduct.Supplier;
        DbProduct.ImageURL = product.ImageURL ?? DbProduct.ImageURL;
        DbProduct.Status = product.Status ?? DbProduct.Status;
        DbProduct.Barcode = product.Barcode ?? DbProduct.Barcode;
        DbProduct.ReorderLevel = product.ReorderLevel ?? DbProduct.ReorderLevel;
        DbProduct.Weight = product.Weight ?? DbProduct.Weight;
        DbProduct.ProductDateUpdated = DateTime.Now;

        await _context.SaveChangesAsync();

        return DbProduct;
    }

    public async Task<ProductBatch?> UpdateBatchAsync(ProductBatch batch)
    {
        var DbBatch = await _context.ProductBatches.SingleOrDefaultAsync(b => b.Id == batch.Id);

        if (DbBatch == null)
        {
            return null;
        }

        // Null fields in the update are left unchanged; the batch stays on its product.
        DbBatch.Lot = batch.Lot ?? DbBatch.Lot;
        DbBatch.Quantity = batch.Quantity ?? DbBatch.Quantity;
        DbBatch.PurchasePrice = batch.PurchasePrice ?? DbBatch.PurchasePrice;
        DbBatch.SellPrice = batch.SellPrice ?? DbBatch.SellPrice;
        DbBatch.ExpirationDate = batch.ExpirationDate ?? DbBatch.ExpirationDate;
        DbBatch.BatchDateUpdated = DateTime.Now;

        await _context.SaveChangesAsync();
        return DbBatch;
    }

    public async Task<Product?> AddBatchAsync(ProductBatch batch, Guid ProductId)
    {
        var product = await _context
            .Products.Include(a => a.Batches)
            .SingleOrDefaultAsync(p => p.Id == ProductId);

        if (product == null)
        {
            return null;
        }

        batch.BatchDateAdded = DateTime.Now;

        product.Batches.Add(batch);

        await _context.SaveChangesAsync();

        return product;
    }

    public async Task<int> GetAllProductsCountAsync()
    {
        return await _context.Products.CountAsync();
    }

    public Task DecrementBatchesAsync(IEnumerable<OrderItemPublishedDto> OrderItems)
    {
        return AdjustBatchQuantitiesAsync(OrderItems, -1);
    }

    public Task IncrementBatchesAsync(IEnumerable<OrderItemPublishedDto> OrderItems)
    {
        return AdjustBatchQuantitiesAsync(OrderItems, 1);
    }

    private async Task AdjustBatchQuantitiesAsync(
        IEnumerable<OrderItemPublishedDto> orderItems,
        int direction
    )
    {
        var quantities = orderItems
            .GroupBy(oi => oi.ItemId)
            .ToDictionary(g => g.Key, g => g.Sum(oi => oi.OrderItemQuantity));
        var ids = quantities.Keys.ToList();

        // Load tracked entities (not a projection) so EF Core detects and saves the quantity changes.
        var batches = await _context.ProductBatches.Where(b => ids.Contains(b.Id)).ToListAsync();

        foreach (var b in batches)
        {
            b.Quantity += direction * quantities[b.Id];
        }

        await _context.SaveChangesAsync();
    }

    public async Task<IEnumerable<Product>> GetAllProductBatchesAsync()
    {
        return await _context
            .Products.AsNoTracking()
            .Include(p => p.Batches)
            .Select(p => new Product { Name = p.Name, Batches = p.Batches })
            .ToArrayAsync();
    }
}
