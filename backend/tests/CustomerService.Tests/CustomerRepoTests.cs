using CustomerService.DataAccess;
using Microsoft.EntityFrameworkCore;
using Sava.Tests.Shared;

namespace CustomerService.Tests;

[Trait("Category", "Integration")]
public class CustomerRepoTests(MySqlFixture mySql) : IAsyncLifetime
{
    private TestDatabase<CustomerContext> _db = null!;

    public async ValueTask InitializeAsync() =>
        _db = await mySql.CreateDatabaseAsync(options => new CustomerContext(options));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Migrations_MatchTheModel()
    {
        await using var context = _db.CreateContext();

        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task CreateCustomerAsync_PersistsCustomerWithGeneratedId()
    {
        var customer = CustomerBuilder.Customer();
        customer.Id = Guid.Empty;

        await using (var context = _db.CreateContext())
        {
            await new CustomerRepo(context).CreateCustomerAsync(customer);
        }

        customer.Id.ShouldNotBe(Guid.Empty);
        await using var assertContext = _db.CreateContext();
        var saved = await new CustomerRepo(assertContext).GetCustomerAsync(customer.Id);
        saved.ShouldNotBeNull().IBAN.ShouldBe(customer.IBAN);
    }

    [Fact]
    public async Task GetAllCustomersAsync_ReturnsAllCustomers()
    {
        await SeedAsync(CustomerBuilder.Customer("A"), CustomerBuilder.Customer("B"));

        await using var context = _db.CreateContext();
        var customers = await new CustomerRepo(context).GetAllCustomersAsync();

        customers.Select(c => c.Name).ShouldBe(["A", "B"], ignoreOrder: true);
    }

    [Fact]
    public async Task GetCustomerAsync_Missing_ReturnsNull()
    {
        await using var context = _db.CreateContext();

        (await new CustomerRepo(context).GetCustomerAsync(Guid.NewGuid())).ShouldBeNull();
    }

    [Fact]
    public async Task UpdateCustomerAsync_OnlyOverwritesSuppliedFields()
    {
        var customer = CustomerBuilder.Customer("Old name");
        await SeedAsync(customer);

        await using (var context = _db.CreateContext())
        {
            var update = CustomerBuilder.UpdateDto(name: "New name", city: "Plovdiv");
            var result = await new CustomerRepo(context).UpdateCustomerAsync(
                Mappings.CustomerMappings.ToEntity(update, customer.Id)
            );
            result.ShouldNotBeNull();
            result.Name.ShouldBe("New name");
            result.City.ShouldBe("Plovdiv");
            result.Email.ShouldBe(customer.Email);
        }

        await using var assertContext = _db.CreateContext();
        var saved = await assertContext.Customers.SingleAsync(
            c => c.Id == customer.Id,
            TestContext.Current.CancellationToken
        );
        saved.Name.ShouldBe("New name");
        saved.City.ShouldBe("Plovdiv");
        saved.Email.ShouldBe(customer.Email);
        saved.IBAN.ShouldBe(customer.IBAN);
    }

    [Fact]
    public async Task UpdateCustomerAsync_Missing_ReturnsNull()
    {
        await using var context = _db.CreateContext();

        var result = await new CustomerRepo(context).UpdateCustomerAsync(
            CustomerBuilder.Customer()
        );

        result.ShouldBeNull();
    }

    [Fact]
    public async Task DeleteCustomerAsync_Existing_RemovesCustomer()
    {
        var customer = CustomerBuilder.Customer();
        await SeedAsync(customer);

        await using (var context = _db.CreateContext())
        {
            (await new CustomerRepo(context).DeleteCustomerAsync(customer.Id)).ShouldNotBeNull();
        }

        await using var assertContext = _db.CreateContext();
        (
            await assertContext.Customers.AnyAsync(TestContext.Current.CancellationToken)
        ).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteCustomerAsync_Missing_ReturnsNull()
    {
        await using var context = _db.CreateContext();

        (await new CustomerRepo(context).DeleteCustomerAsync(Guid.NewGuid())).ShouldBeNull();
    }

    private async Task SeedAsync(params Models.Customer[] customers)
    {
        await using var context = _db.CreateContext();
        context.Customers.AddRange(customers);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
