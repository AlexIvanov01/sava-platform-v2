using System;
using InventoryService.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace InventoryService.Auditing;

public static class AuditStamper
{
    // Created* is only filled when missing so seed data can carry its own dates;
    // Updated* is always overwritten on modification.
    public static void Stamp(ChangeTracker changeTracker, ICurrentUser? currentUser)
    {
        var now = DateTime.UtcNow;
        var user = currentUser?.Name ?? HttpContextCurrentUser.SystemName;

        foreach (var entry in changeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt ??= now;
                    entry.Entity.CreatedBy ??= user;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    entry.Entity.UpdatedBy = user;
                    break;
            }
        }
    }
}
