using System.Threading;
using System.Threading.Tasks;
using CustomerService.Auditing;
using CustomerService.Models;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.DataAccess;

public class CustomerContext : DbContext
{
    private readonly ICurrentUser? _currentUser;

    // currentUser is optional so tests and tooling can construct the context from options alone.
    public CustomerContext(DbContextOptions options, ICurrentUser? currentUser = null)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<Customer> Customers { get; set; }

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
}
