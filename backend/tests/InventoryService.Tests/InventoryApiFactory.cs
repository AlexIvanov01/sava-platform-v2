using InventoryService.AsyncDataServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

// Runs the real app against its own MySQL database; RabbitMQ (publisher and subscriber) is replaced.
public sealed class InventoryApiFactory(MySqlFixture mySql) : WebApplicationFactory<Program>
{
    private readonly string _connectionString = mySql.GetConnectionString(
        MySqlFixture.NewDatabaseName("api")
    );

    public IMessageBusClient MessageBus { get; } = Substitute.For<IMessageBusClient>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMessageBusClient>();
            services.AddSingleton(MessageBus);
            foreach (
                var hosted in services
                    .Where(d => d.ImplementationType == typeof(MessageBusSubscriber))
                    .ToList()
            )
            {
                services.Remove(hosted);
            }
        });
    }
}
