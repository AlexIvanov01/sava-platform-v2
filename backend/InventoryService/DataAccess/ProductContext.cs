using System.Threading;
using System.Threading.Tasks;
using InventoryService.Auditing;
using InventoryService.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryService.DataAccess;

public class ProductContext : DbContext
{
    private readonly ICurrentUser? _currentUser;

    // currentUser is optional so tests and tooling can construct the context from options alone.
    public ProductContext(DbContextOptions options, ICurrentUser? currentUser = null)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<Product> Products { get; set; }
    public DbSet<ProductBatch> ProductBatches { get; set; }
    public DbSet<Category> Categories { get; set; }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AuditStamper.Stamp(ChangeTracker, _currentUser);
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        AuditStamper.Stamp(ChangeTracker, _currentUser);
        return base.SaveChanges();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<Product>()
            .HasMany(p => p.Batches)
            .WithOne(b => b.Product)
            .HasForeignKey(b => b.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Product>().HasIndex(p => p.SKU).IsUnique();

        modelBuilder
            .Entity<Product>()
            .Property(p => p.UnitOfMeasure)
            .HasConversion<string>()
            .HasMaxLength(20);

        // A category with products can't be deleted; CategoryRepo reports that as a conflict.
        modelBuilder
            .Entity<Category>()
            .HasMany(c => c.Products)
            .WithOne(p => p.Category)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Category>().HasIndex(c => c.Name).IsUnique();
    }
}
