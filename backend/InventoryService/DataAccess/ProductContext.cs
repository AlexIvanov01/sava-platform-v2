using System.Threading;
using System.Threading.Tasks;
using InventoryService.Auditing;
using InventoryService.Messaging;
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
    public DbSet<StockMovement> StockMovements { get; set; }
    public DbSet<InboxMessage> InboxMessages { get; set; }

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

        modelBuilder
            .Entity<ProductBatch>()
            .HasMany(b => b.Movements)
            .WithOne(m => m.Batch)
            .HasForeignKey(m => m.BatchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<StockMovement>(movement =>
        {
            movement.Property(m => m.Type).HasConversion<string>().HasMaxLength(20);
            movement.Property(m => m.Reason).HasConversion<string>().HasMaxLength(20);
            // Order events reconcile per (order, batch).
            movement.HasIndex(m => new { m.OrderId, m.BatchId });
        });

        modelBuilder.Entity<InboxMessage>(message =>
        {
            message.Property(m => m.Status).HasConversion<string>();
            message.Property(m => m.Sequence).ValueGeneratedOnAdd();
            message.HasIndex(m => m.Sequence).IsUnique();
            message.HasIndex(m => new { m.Status, m.CreatedAt });
            message.HasIndex(m => m.AggregateId);
        });
    }
}
