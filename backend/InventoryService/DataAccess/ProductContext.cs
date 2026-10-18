using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using InventoryService.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.DataAccess;

public class ProductContext : DbContext
{
    public ProductContext(DbContextOptions options)
        : base(options) { }

    public DbSet<Product> Products { get; set; }
    public DbSet<ProductBatch> ProductBatches { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<Product>()
            .HasMany(p => p.Batches)
            .WithOne(b => b.Product)
            .HasForeignKey(b => b.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
