using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OrderService.Auditing;
using OrderService.Models;

namespace OrderService.DataAccess;

// currentUser is optional so tests and tooling can construct the context from options alone.
public class OrderContext(DbContextOptions options, ICurrentUser? currentUser = null)
    : DbContext(options)
{
    public DbSet<Order> Orders { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<OrderItemBatch> OrderItemBatches { get; set; }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AuditStamper.Stamp(ChangeTracker, currentUser);
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        AuditStamper.Stamp(ChangeTracker, currentUser);
        return base.SaveChanges();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>().Property(o => o.Status).HasConversion<string>();
        modelBuilder.Entity<Order>().OwnsOne(o => o.Customer);
        // Every order has a customer snapshot, even if all its fields are empty.
        modelBuilder.Entity<Order>().Navigation(o => o.Customer).IsRequired();

        modelBuilder
            .Entity<OrderItem>()
            .HasOne(l => l.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(l => l.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<OrderItem>().HasIndex(l => new { l.OrderId, l.ProductId }).IsUnique();

        modelBuilder
            .Entity<OrderItemBatch>()
            .HasOne(b => b.Item)
            .WithMany(l => l.Batches)
            .HasForeignKey(b => b.OrderItemId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder
            .Entity<OrderItemBatch>()
            .HasIndex(b => new { b.OrderItemId, b.BatchId })
            .IsUnique();

        modelBuilder
            .Entity<Order>()
            .HasOne(o => o.Invoice)
            .WithOne(i => i.Order)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
