using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OrderService.AsyncDataServices;
using OrderService.Models;
using OrderService.SyncDataServices.Grpc;
using Sava.Tests.Shared;

namespace OrderService.Tests;

// Runs the real app against its own MySQL database. RabbitMQ and the gRPC clients are replaced;
// the gRPC substitutes return the customer and items that the startup sync seeds into the database.
public class OrderApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public OrderApiFactory(MySqlFixture mySql)
    {
        _connectionString = mySql.GetConnectionString(MySqlFixture.NewDatabaseName("api"));
        InventoryClient.ReturnAllItems().Returns(_ => Items.Select(Copy).ToList());
        CustomerClient.ReturnAllCustomers().Returns(_ => [Customer]);
    }

    public IMessageBusClient MessageBus { get; } = Substitute.For<IMessageBusClient>();

    protected virtual string HostEnvironment => Environments.Development;
    public IInventoryDataClient InventoryClient { get; } = Substitute.For<IInventoryDataClient>();
    public ICustomerDataClient CustomerClient { get; } = Substitute.For<ICustomerDataClient>();
    public Customer Customer { get; } = OrderBuilder.Customer();
    public Item[] Items { get; } =
    [OrderBuilder.Item("Synced A", 2m), OrderBuilder.Item("Synced B", 3m)];

    private static Item Copy(Item i) =>
        new()
        {
            ExternalBatchId = i.ExternalBatchId,
            ExternalProductId = i.ExternalProductId,
            Name = i.Name,
            Price = i.Price,
            Lot = i.Lot,
            ExpirationDate = i.ExpirationDate,
        };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironment);
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMessageBusClient>();
            services.AddSingleton(MessageBus);
            services.RemoveAll<IInventoryDataClient>();
            services.AddScoped(_ => InventoryClient);
            services.RemoveAll<ICustomerDataClient>();
            services.AddScoped(_ => CustomerClient);
            var background = new[] { typeof(MessageBusSubscriber), typeof(GrpcSyncService) };
            foreach (
                var hosted in services
                    .Where(d => background.Contains(d.ImplementationType))
                    .ToList()
            )
            {
                services.Remove(hosted);
            }
        });
    }
}
