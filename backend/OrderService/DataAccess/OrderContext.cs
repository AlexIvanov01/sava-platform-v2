using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OrderService.Auditing;
using OrderService.Messaging;
using OrderService.Models;

namespace OrderService.DataAccess;

// currentUser and outboxSignal are optional so tests and tooling can construct the context from
// options alone.
public class OrderContext(
    DbContextOptions options,
    ICurrentUser? currentUser = null,
    OutboxSignal? outboxSignal = null
) : DbContext(options)
{
    public DbSet<Order> Orders { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<OrderItemBatch> OrderItemBatches { get; set; }
    public DbSet<CompanySettings> CompanySettings { get; set; }
    public DbSet<OutboxMessage> OutboxMessages { get; set; }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AuditStamper.Stamp(ChangeTracker, currentUser);
        var raised = AddOutboxMessages();
        var result = await base.SaveChangesAsync(cancellationToken);
        Raised(raised);
        return result;
    }

    public override int SaveChanges()
    {
        AuditStamper.Stamp(ChangeTracker, currentUser);
        var raised = AddOutboxMessages();
        var result = base.SaveChanges();
        Raised(raised);
        return result;
    }

    // Wakes OutboxPublisher. A save inside an explicit transaction may signal before the commit;
    // its message is then sent by the next sweep.
    private void Raised(List<Order> raised)
    {
        raised.ForEach(o => o.ClearEvents());
        if (raised.Count > 0)
        {
            outboxSignal?.Notify();
        }
    }

    // Each order with raised events gets one outbox message, for the newest event, in the same
    // transaction as the order change. A deleted order's items are still loaded at this point.
    private List<Order> AddOutboxMessages()
    {
        var raised = ChangeTracker
            .Entries<Order>()
            .Select(e => e.Entity)
            .Where(o => o.Events.Count > 0)
            .ToList();
        var now = DateTime.UtcNow;
        foreach (var order in raised)
        {
            OutboxMessages.Add(order.ToOutboxMessage(order.Events[^1], now));
        }
        return raised;
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

        modelBuilder.Entity<Invoice>(invoice =>
        {
            invoice.Property(i => i.PaymentMethod).HasConversion<string>();
            invoice.Property(i => i.PaymentStatus).HasConversion<string>();
            invoice.OwnsOne(i => i.Supplier);
            invoice.Navigation(i => i.Supplier).IsRequired();
            invoice.OwnsOne(i => i.Customer);
            invoice.Navigation(i => i.Customer).IsRequired();
            // The owned rows' int Id alone is their key: MySQL rejects an auto-increment column
            // that isn't the first column of a key, as in the default (InvoiceId, Id).
            invoice.OwnsMany(
                i => i.Items,
                item =>
                {
                    item.ToTable("InvoiceItems");
                    item.HasKey("Id");
                }
            );
            invoice.OwnsMany(
                i => i.VatTotals,
                total =>
                {
                    total.ToTable("InvoiceVatTotals");
                    total.HasKey("Id");
                }
            );
            invoice.HasIndex(i => i.Number);
        });

        modelBuilder
            .Entity<Order>()
            .HasOne(o => o.Invoice)
            .WithOne(i => i.Order)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<OutboxMessage>(message =>
        {
            message.Property(m => m.Status).HasConversion<string>();
            message.Property(m => m.Sequence).ValueGeneratedOnAdd();
            message.HasIndex(m => m.Sequence).IsUnique();
            message.HasIndex(m => new { m.Status, m.NextAttemptAt });
            message.HasIndex(m => m.AggregateId);
        });
    }
}
