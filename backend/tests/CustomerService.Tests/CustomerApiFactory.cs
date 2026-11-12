using CustomerService.AsyncDataServices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sava.Tests.Shared;

namespace CustomerService.Tests;

// Runs the real app against its own MySQL database; the RabbitMQ subscriber is removed.
public class CustomerApiFactory(MySqlFixture mySql) : WebApplicationFactory<Program>
{
    private readonly string _connectionString = mySql.GetConnectionString(
        MySqlFixture.NewDatabaseName("api")
    );

    protected virtual string HostEnvironment => Environments.Development;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironment);
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.ConfigureTestServices(services =>
        {
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
