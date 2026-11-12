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
        await _context.Products.AddAsync(product);
        await _context.SaveChangesAsync();
        await _context.Entry(product).Reference(p => p.Category).LoadAsync();
    }

    public async Task<(DeleteStatus Status, Product? Product)> DeleteProductAsync(Guid id)
    {
        var dbProduct = await _context
            .Products.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id);

        if (dbProduct == null)
        {
            return (DeleteStatus.NotFound, null);
        }

        if (dbProduct.HasOrders)
        {
            return (DeleteStatus.HasOrders, dbProduct);
        }

        _context.Products.Attach(dbProduct);
        _context.Products.Remove(dbProduct);

        await _context.SaveChangesAsync();

        return (DeleteStatus.Deleted, dbProduct);
    }

    public async Task<(DeleteStatus Status, ProductBatch? Batch)> DeleteBatchAsync(Guid id)
    {
        var dbBatch = await _context
            .ProductBatches.AsNoTracking()
            .SingleOrDefaultAsync(b => b.Id == id);

        if (dbBatch == null)
        {
            return (DeleteStatus.NotFound, null);
        }

        if (dbBatch.HasOrders)
        {
            return (DeleteStatus.HasOrders, dbBatch);
        }

        _context.ProductBatches.Attach(dbBatch);
        _context.ProductBatches.Remove(dbBatch);

        await _context.SaveChangesAsync();

        return (DeleteStatus.Deleted, dbBatch);
    }

    public async Task<IEnumerable<Product>> GetAllProductsAsync(
        ProductFilter filter,
        Guid? cursor,
        int pageSize
    )
    {
        var query = Filter(_context.Products.AsNoTracking(), filter);

        if (cursor != null)
        {
            query = query.Where(p => p.Id > cursor);
        }

        return await query
            .Include(p => p.Category)
            .Include(p => p.Batches)
            .OrderBy(p => p.Id)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<bool> SkuExistsAsync(string sku, Guid? exceptProductId = null) =>
        exceptProductId == null
            ? await _context.Products.AnyAsync(p => p.SKU == sku)
            : await _context.Products.AnyAsync(p => p.SKU == sku && p.Id != exceptProductId);

    // Name and SKU match as substrings (case-insensitive under MySQL's default collation);
    // barcodes come from a scanner, so they match exactly.
    private static IQueryable<Product> Filter(IQueryable<Product> query, ProductFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name))
        {
            query = query.Where(p => p.Name!.Contains(filter.Name));
        }
        if (!string.IsNullOrWhiteSpace(filter.SKU))
        {
            query = query.Where(p => p.SKU!.Contains(filter.SKU));
        }
        if (!string.IsNullOrWhiteSpace(filter.Barcode))
        {
            query = query.Where(p => p.Barcode == filter.Barcode);
        }
        if (filter.CategoryId != null)
        {
            query = query.Where(p => p.CategoryId == filter.CategoryId);
        }
        if (filter.IsPublished != null)
        {
            query = query.Where(p => (p.IsPublished ?? false) == filter.IsPublished);
        }
        return query;
    }

    public async Task<Product?> GetProductAsync(Guid id)
    {
        var product = await _context
            .Products.AsNoTracking()
            .Include(p => p.Category)
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
        DbProduct.SKU = product.SKU ?? DbProduct.SKU;
        DbProduct.ListPrice = product.ListPrice ?? DbProduct.ListPrice;
        DbProduct.VatRate = product.VatRate ?? DbProduct.VatRate;
        DbProduct.UnitOfMeasure = product.UnitOfMeasure ?? DbProduct.UnitOfMeasure;
        DbProduct.IsPublished = product.IsPublished ?? DbProduct.IsPublished;
        DbProduct.CategoryId = product.CategoryId ?? DbProduct.CategoryId;

        await _context.SaveChangesAsync();
        await _context.Entry(DbProduct).Reference(p => p.Category).LoadAsync();
        // The response includes the batches and the cost/stock figures derived from them.
        await _context.Entry(DbProduct).Collection(p => p.Batches).LoadAsync();

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
        // Editing the quantity directly is recorded as a count correction in the ledger.
        var difference = (batch.Quantity ?? DbBatch.Quantity ?? 0) - (DbBatch.Quantity ?? 0);
        if (difference != 0)
        {
            DbBatch.Move(
                StockMovementType.Adjustment,
                difference,
                AdjustmentReason.CountCorrection,
                note: "Quantity edited on the batch"
            );
        }
        DbBatch.PurchasePrice = batch.PurchasePrice ?? DbBatch.PurchasePrice;
        DbBatch.ExpirationDate = batch.ExpirationDate ?? DbBatch.ExpirationDate;
        DbBatch.Location = batch.Location ?? DbBatch.Location;

        await _context.SaveChangesAsync();
        return DbBatch;
    }

    public async Task<Product?> AddBatchAsync(ProductBatch batch, Guid ProductId)
    {
        var product = await _context
            .Products.Include(p => p.Category)
            .Include(a => a.Batches)
            .SingleOrDefaultAsync(p => p.Id == ProductId);

        if (product == null)
        {
            return null;
        }

        // The initial quantity enters the ledger as a receipt.
        var received = batch.Quantity ?? 0;
        batch.Quantity = 0;
        product.Batches.Add(batch);
        if (received != 0)
        {
            batch.Move(StockMovementType.Receipt, received);
        }

        await _context.SaveChangesAsync();

        return product;
    }

    public async Task<int> GetAllProductsCountAsync(ProductFilter filter)
    {
        return await Filter(_context.Products, filter).CountAsync();
    }

    public async Task<List<Product>> GetProductsWithBatchesAsync(Guid? categoryId = null)
    {
        var query = _context.Products.AsNoTracking();
        if (categoryId != null)
        {
            query = query.Where(p => p.CategoryId == categoryId);
        }
        return await query.Include(p => p.Category).Include(p => p.Batches).ToListAsync();
    }

    public async Task<List<Product>> GetProductsByIdsAsync(IReadOnlyCollection<Guid> ids) =>
        await _context
            .Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Include(p => p.Batches)
            .ToListAsync();

    public async Task<List<ProductBatch>> GetBatchesByIdsAsync(IReadOnlyCollection<Guid> ids) =>
        await _context.ProductBatches.AsNoTracking().Where(b => ids.Contains(b.Id)).ToListAsync();

    public async Task<List<ProductBatch>> GetBatchesInStockExpiringByAsync(DateTime until) =>
        await _context
            .ProductBatches.AsNoTracking()
            .Where(b => b.Quantity > 0 && b.ExpirationDate < until.Date.AddDays(1))
            .Include(b => b.Product)
            .ToListAsync();
}
