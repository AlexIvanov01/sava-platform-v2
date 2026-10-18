using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OrderService.DataAccess;
using OrderService.Models;
using OrderService.SyncDataServices.Grpc;

namespace OrderService.Tests;

public class GrpcSyncServiceTests
{
    [Fact]
    public async Task ExecuteAsync_SyncsItemsAndCustomersFromGrpcClients()
    {
        var inventory = Substitute.For<IInventoryDataClient>();
        var customerClient = Substitute.For<ICustomerDataClient>();
        var itemRepo = Substitute.For<IItemRepo>();
        var customerRepo = Substitute.For<ICustomerRepo>();
        Item[] items = [OrderBuilder.Item()];
        Customer[] customers = [OrderBuilder.Customer()];
        inventory.ReturnAllItems().Returns(items);
        customerClient.ReturnAllCustomers().Returns(customers);
        var synced = new TaskCompletionSource();
        customerRepo
            .When(r => r.SyncCustomersAsync(Arg.Any<IEnumerable<Customer>>()))
            .Do(_ => synced.TrySetResult());

        var provider = new ServiceCollection()
            .AddScoped(_ => inventory)
            .AddScoped(_ => customerClient)
            .AddScoped(_ => itemRepo)
            .AddScoped(_ => customerRepo)
            .BuildServiceProvider();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?> { ["GrpcSyncIntervalInMinutes"] = "0" }
            )
            .Build();
        using var service = new GrpcSyncService(provider, config);

        await service.StartAsync(TestContext.Current.CancellationToken);
        await synced.Task.WaitAsync(
            TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken
        );
        await service.StopAsync(TestContext.Current.CancellationToken);

        await itemRepo.Received().SyncItemsAsync(items);
        await customerRepo.Received().SyncCustomersAsync(customers);
    }
}
