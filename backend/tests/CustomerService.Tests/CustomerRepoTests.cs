using CustomerService.DataAccess;
using CustomerService.Dtos;
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
            (await new CustomerRepo(context).DeleteCustomerAsync(customer.Id)).Status.ShouldBe(
                DeleteCustomerStatus.Deleted
            );
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

        (await new CustomerRepo(context).DeleteCustomerAsync(Guid.NewGuid())).Status.ShouldBe(
            DeleteCustomerStatus.NotFound
        );
    }

    [Fact]
    public async Task OrderReferences_FollowPublishedAndDeletedOrders()
    {
        var first = CustomerBuilder.Customer();
        var second = CustomerBuilder.Customer();
        await SeedAsync(first, second);
        var order = TestData.Faker.Random.Guid();

        await using var context = _db.CreateContext();
        var repo = new CustomerRepo(context);

        await repo.SetOrderCustomerAsync(order, first.Id);
        await repo.SetOrderCustomerAsync(order, first.Id); // redelivery
        (await repo.HasOrdersAsync(first.Id)).ShouldBeTrue();
        (await repo.DeleteCustomerAsync(first.Id)).Status.ShouldBe(DeleteCustomerStatus.HasOrders);

        await repo.SetOrderCustomerAsync(order, second.Id); // order moved to another customer
        (await repo.HasOrdersAsync(first.Id)).ShouldBeFalse();
        (await repo.HasOrdersAsync(second.Id)).ShouldBeTrue();

        await repo.RemoveOrderAsync(order);
        await repo.RemoveOrderAsync(order); // redelivery
        (await repo.HasOrdersAsync(second.Id)).ShouldBeFalse();
        (await repo.DeleteCustomerAsync(second.Id)).Status.ShouldBe(DeleteCustomerStatus.Deleted);
    }

    [Fact]
    public async Task GetCustomersAsync_PagesFiltersAndFlagsCustomersWithOrders()
    {
        var acme = CustomerBuilder.Customer("Ivan Petrov");
        acme.CompanyName = "Acme Trading Ltd";
        acme.UIC = "201234567";
        acme.VATNumber = "BG201234567";
        var other = CustomerBuilder.Customer("Maria Ivanova");
        other.CompanyName = "Other OOD";
        await SeedAsync(acme, other);
        await using (var context = _db.CreateContext())
        {
            await new CustomerRepo(context).SetOrderCustomerAsync(
                TestData.Faker.Random.Guid(),
                acme.Id
            );
        }

        await using var readContext = _db.CreateContext();
        var repo = new CustomerRepo(readContext);

        var byCompany = (
            await repo.GetCustomersAsync(new CustomerFilter(Name: "acme"), null, 10)
        ).ToList();
        byCompany.ShouldHaveSingleItem().Customer.Id.ShouldBe(acme.Id);
        byCompany[0].HasOrders.ShouldBeTrue();
        (await repo.GetCustomersAsync(new CustomerFilter(Name: "ivan"), null, 10))
            .Count()
            .ShouldBe(2); // "Ivan Petrov" and "Maria Ivanova"
        (await repo.GetCustomersAsync(new CustomerFilter(UIC: "2012"), null, 10))
            .ShouldHaveSingleItem()
            .HasOrders.ShouldBeTrue();
        (
            await repo.GetCustomersAsync(new CustomerFilter(VATNumber: "BG2012"), null, 10)
        ).ShouldHaveSingleItem();
        (await repo.GetCustomersCountAsync(new CustomerFilter(Name: "ivan"))).ShouldBe(2);

        var all = (await repo.GetCustomersAsync(CustomerFilter.None, null, 1)).ToList();
        var next = (
            await repo.GetCustomersAsync(CustomerFilter.None, all[0].Customer.Id, 1)
        ).ToList();
        next.ShouldHaveSingleItem().Customer.Id.ShouldNotBe(all[0].Customer.Id);
        (await repo.GetCustomersAsync(CustomerFilter.None, next[0].Customer.Id, 1)).ShouldBeEmpty();
    }

    private async Task SeedAsync(params Models.Customer[] customers)
    {
        await using var context = _db.CreateContext();
        context.Customers.AddRange(customers);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
