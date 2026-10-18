using Microsoft.EntityFrameworkCore;
using OrderService.DataAccess;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class CustomerRepoTests(MySqlFixture mySql) : OrderDbTestBase(mySql)
{
    [Fact]
    public async Task AddCustomerAsync_PersistsCustomer_SkipsEmptyId()
    {
        var customer = OrderBuilder.Customer();

        await using (var context = Db.CreateContext())
        {
            var repo = new CustomerRepo(context);
            await repo.AddCustomerAsync(customer);
            await repo.AddCustomerAsync(new Customer { ExternalId = Guid.Empty, Name = "Nobody" });
            (await repo.ExternalCustomerExistsAsync(customer.ExternalId)).ShouldBeTrue();
        }

        await using var assert = Db.CreateContext();
        (await new CustomerRepo(assert).GetCustomerAsync(customer.ExternalId))
            .ShouldNotBeNull()
            .IBAN.ShouldBe(customer.IBAN);
        (await assert.Customers.CountAsync(Ct)).ShouldBe(1);
    }

    [Fact]
    public async Task UpdateCustomerAsync_UpdatesSuppliedFields()
    {
        var customer = OrderBuilder.Customer();
        await SeedAsync(customer);

        await using (var context = Db.CreateContext())
        {
            await new CustomerRepo(context).UpdateCustomerAsync(
                new Customer { ExternalId = customer.ExternalId, City = "Plovdiv" }
            );
            await new CustomerRepo(context).UpdateCustomerAsync(
                new Customer { ExternalId = Guid.NewGuid(), City = "Nowhere" }
            );
        }

        await using var assert = Db.CreateContext();
        var saved = await assert.Customers.SingleAsync(Ct);
        saved.City.ShouldBe("Plovdiv");
        saved.Name.ShouldBe(customer.Name);
    }

    [Fact]
    public async Task DeleteCustomerAsync_RemovesCustomer()
    {
        var customer = OrderBuilder.Customer();
        await SeedAsync(customer);

        await using (var context = Db.CreateContext())
        {
            await new CustomerRepo(context).DeleteCustomerAsync(customer.ExternalId);
        }

        await using var assert = Db.CreateContext();
        (await assert.Customers.AnyAsync(Ct)).ShouldBeFalse();
    }

    [Fact]
    public async Task SyncCustomersAsync_AddsUpdatesAndRemovesStaleCustomersWithoutOrders()
    {
        var (withOrders, _, _) = await SeedOrderAsync(1);
        var stale = OrderBuilder.Customer("Stale");
        var existing = OrderBuilder.Customer("Existing");
        await SeedAsync(stale, existing);
        var incoming = OrderBuilder.Customer("Incoming");
        var updated = OrderBuilder.Customer("Existing v2");
        updated.ExternalId = existing.ExternalId;

        await using (var context = Db.CreateContext())
        {
            await new CustomerRepo(context).SyncCustomersAsync([incoming, updated]);
        }

        await using var assert = Db.CreateContext();
        var names = await assert.Customers.ToDictionaryAsync(c => c.ExternalId, c => c.Name, Ct);
        names.Keys.ShouldBe(
            [withOrders.ExternalId, existing.ExternalId, incoming.ExternalId],
            ignoreOrder: true
        );
        names[existing.ExternalId].ShouldBe("Existing v2");
    }
}
