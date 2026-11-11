using System.Threading;
using System.Threading.Tasks;
using AuthenticationService.Auditing;
using AuthenticationService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthenticationService.DataAccess;

// currentUser is optional so tests and tooling can construct the context from options alone.
public class AuthContext(DbContextOptions options, ICurrentUser? currentUser = null)
    : DbContext(options)
{
    public DbSet<User> Users { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }

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
        modelBuilder.Entity<User>().HasIndex(u => u.UserName).IsUnique();

        modelBuilder.Entity<RefreshToken>().HasIndex(t => t.TokenHash).IsUnique();

        modelBuilder
            .Entity<User>()
            .HasMany(u => u.RefreshTokens)
            .WithOne(t => t.User)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
