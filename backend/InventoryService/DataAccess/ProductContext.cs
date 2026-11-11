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
    }
}
