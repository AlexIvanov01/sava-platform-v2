using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OrderService.AsyncDataServices;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.Messaging;
using OrderService.Models;
using OrderService.SyncDataServices.Grpc;
using Sava.Tests.Shared;

namespace OrderService.Tests;

// Runs the real app against its own MySQL database. The outbox publisher and cleanup don't run
// unless RunsOutboxPublisher (tests read the outbox table instead). RabbitMQ and the gRPC clients are
// replaced: by default the Customer client knows one customer and the Inventory client answers from a
// small catalog of two products with two batches each. Tests can reconfigure the substitutes.
public class OrderApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public OrderApiFactory(MySqlFixture mySql)
    {
        _connectionString = mySql.GetConnectionString(MySqlFixture.NewDatabaseName("api"));
        Products = [OrderBuilder.Product(), OrderBuilder.Product()];
        Batches =
        [
            .. Products.SelectMany(p =>
                new[] { OrderBuilder.Batch(p.Id), OrderBuilder.Batch(p.Id) }
            ),
        ];
        CustomerClient.GetCustomerAsync(CustomerId).Returns(_ => Customer);
        InventoryClient.Serves(OrderBuilder.Catalog(Products, Batches));
    }

    protected virtual string HostEnvironment => Environments.Development;

    protected virtual bool RunsOutboxPublisher => false;

    public IMessageBusClient MessageBus { get; } = Substitute.For<IMessageBusClient>();

    // The events written to the outbox for the order, oldest first.
    public async Task<List<OrderPublishedEvent>> OutboxFor(Guid orderId)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderContext>();
        var payloads = await db
            .OutboxMessages.Where(m => m.AggregateId == orderId)
            .OrderBy(m => m.Sequence)
            .Select(m => m.Payload!)
            .ToListAsync(TestContext.Current.CancellationToken);
        return [.. payloads.Select(p => JsonSerializer.Deserialize<OrderPublishedEvent>(p)!)];
    }

    public IInventoryDataClient InventoryClient { get; } = Substitute.For<IInventoryDataClient>();
    public ICustomerDataClient CustomerClient { get; } = Substitute.For<ICustomerDataClient>();

    public Guid CustomerId { get; } = TestData.Faker.Random.Guid();
    public CustomerSnapshot Customer { get; } = OrderBuilder.Customer();
    public ProductSnapshot[] Products { get; }

    // Products[i] has Batches[2 * i] and Batches[2 * i + 1].
    public BatchSnapshot[] Batches { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironment);
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.ConfigureTestServices(services =>
        {
            if (!RunsOutboxPublisher)
            {
                foreach (
                    var hosted in services
                        .Where(d =>
                            d.ImplementationType == typeof(OutboxPublisher)
                            || d.ImplementationType == typeof(OutboxCleanup)
                        )
                        .ToList()
                )
                {
                    services.Remove(hosted);
                }
            }
            services.RemoveAll<IMessageBusClient>();
            services.AddSingleton(MessageBus);
            services.RemoveAll<IInventoryDataClient>();
            services.AddSingleton(InventoryClient);
            services.RemoveAll<ICustomerDataClient>();
            services.AddSingleton(CustomerClient);
        });
    }
}
