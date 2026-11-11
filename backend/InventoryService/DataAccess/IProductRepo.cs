using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using InventoryService.Dtos;
using InventoryService.Models;

namespace InventoryService.DataAccess;

public interface IProductRepo
{
    Task<int> GetAllProductsCountAsync(ProductFilter filter);
    Task<IEnumerable<Product>> GetAllProductsAsync(
        ProductFilter filter,
        Guid? cursor,
        int pageSize
    );
    Task<bool> SkuExistsAsync(string sku, Guid? exceptProductId = null);
    Task<Product?> GetProductAsync(Guid id);
    Task CreateProductAsync(Product product);
    Task<Product?> DeleteProductAsync(Guid id);
    Task<Product?> UpdateProductAsync(Product product);
    Task<Product?> AddBatchAsync(ProductBatch batch, Guid ProductId);
    Task<ProductBatch?> UpdateBatchAsync(ProductBatch batch);
    Task<ProductBatch?> DeleteBatchAsync(Guid id);
    Task<IEnumerable<Product>> GetAllProductBatchesAsync();

    // For reports: every product (optionally of one category) with its category and batches.
    Task<List<Product>> GetProductsWithBatchesAsync(Guid? categoryId = null);

    // For reports: batches with stock expiring on or before `until`, with their products.
    Task<List<ProductBatch>> GetBatchesInStockExpiringByAsync(DateTime until);
}
