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

    // Refused (HasOrders) while orders hold stock from the product or batch.
    Task<(DeleteStatus Status, Product? Product)> DeleteProductAsync(Guid id);
    Task<Product?> UpdateProductAsync(Product product);
    Task<Product?> AddBatchAsync(ProductBatch batch, Guid ProductId);
    Task<ProductBatch?> UpdateBatchAsync(ProductBatch batch);
    Task<(DeleteStatus Status, ProductBatch? Batch)> DeleteBatchAsync(Guid id);

    // For reports: every product (optionally of one category) with its category and batches.
    Task<List<Product>> GetProductsWithBatchesAsync(Guid? categoryId = null);

    // Untracked products with their batches; ids that don't exist are left out.
    Task<List<Product>> GetProductsByIdsAsync(IReadOnlyCollection<Guid> ids);

    // Untracked; ids that don't exist are left out.
    Task<List<ProductBatch>> GetBatchesByIdsAsync(IReadOnlyCollection<Guid> ids);

    // For reports: batches with stock expiring on or before `until`, with their products.
    Task<List<ProductBatch>> GetBatchesInStockExpiringByAsync(DateTime until);
}

public enum DeleteStatus
{
    NotFound,
    Deleted,
    HasOrders,
}
