using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Sava.Tests.Shared;

namespace AuthenticationService.Tests;

// Runs the real app against its own MySQL database in the Production environment, so the
// signing key and the seeded administrator come only from settings (appsettings.Development.json is skipped).
public class AuthApiFactory(MySqlFixture mySql) : WebApplicationFactory<Program>
{
    public const string AdminUserName = "admin";
    public static readonly string AdminPassword = "Adm1n-" + TestData.RunSeed;

    private readonly string _connectionString = mySql.GetConnectionString(
        MySqlFixture.NewDatabaseName("api")
    );

    protected virtual string? SigningKey => TestJwt.Key;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("Jwt:Key", SigningKey);
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Seed:AdminUserName", AdminUserName);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
    }
}

public sealed class MissingKeyAuthApiFactory(MySqlFixture mySql) : AuthApiFactory(mySql)
{
    protected override string? SigningKey => null;
}
