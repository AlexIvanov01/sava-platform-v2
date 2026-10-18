using CustomerService.AsyncDataServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sava.Tests.Shared;

namespace CustomerService.Tests;

// Runs the real app against its own MySQL database; RabbitMQ is replaced with a substitute.
public class CustomerApiFactory(MySqlFixture mySql) : WebApplicationFactory<Program>
{
    private readonly string _connectionString = mySql.GetConnectionString(
        MySqlFixture.NewDatabaseName("api")
    );

    public IMessageBusClient MessageBus { get; } = Substitute.For<IMessageBusClient>();

    protected virtual string HostEnvironment => Environments.Development;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironment);
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IMessageBusClient>();
            services.AddSingleton(MessageBus);
        });
    }
}
