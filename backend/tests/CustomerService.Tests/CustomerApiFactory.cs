using CustomerService.AsyncDataServices;
using CustomerService.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sava.Tests.Shared;

namespace CustomerService.Tests;

// Runs the real app against its own MySQL database. The RabbitMQ subscriber and the inbox cleanup
// are removed; tests hand messages to IInboxHandler, as the subscriber would.
public class CustomerApiFactory(MySqlFixture mySql) : WebApplicationFactory<Program>
{
    private readonly string _connectionString = mySql.GetConnectionString(
        MySqlFixture.NewDatabaseName("api")
    );

    protected virtual string HostEnvironment => Environments.Development;

    protected virtual IReadOnlyDictionary<string, string> Settings { get; } =
        new Dictionary<string, string>();

    protected virtual void ConfigureServices(IServiceCollection services) { }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(HostEnvironment);
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }
        builder.ConfigureTestServices(services =>
        {
            foreach (
                var hosted in services
                    .Where(d =>
                        d.ImplementationType == typeof(MessageBusSubscriber)
                        || d.ImplementationType == typeof(InboxCleanup)
                    )
                    .ToList()
            )
            {
                services.Remove(hosted);
            }
            ConfigureServices(services);
        });
    }
}
