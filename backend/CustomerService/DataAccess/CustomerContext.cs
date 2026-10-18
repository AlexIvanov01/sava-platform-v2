using CustomerService.Models;
using Microsoft.EntityFrameworkCore;

namespace CustomerService.DataAccess;

public class CustomerContext : DbContext
{
    public CustomerContext(DbContextOptions options)
        : base(options) { }

    public DbSet<Customer> Customers { get; set; }
}
