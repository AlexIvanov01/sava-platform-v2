using Microsoft.EntityFrameworkCore;
using Testcontainers.MySql;

namespace Sava.Tests.Shared;

// One MySQL container per test assembly (registered with [assembly: AssemblyFixture]).
// Each test gets its own database on it, so tests stay isolated and can run in parallel.
public sealed class MySqlFixture : IAsyncLifetime
{
    private const string Password = "Test_pass1";

    private readonly MySqlContainer _container = new MySqlBuilder("mysql:9.7")
        .WithUsername("root")
        .WithPassword(Password)
        .Build();

    public ValueTask InitializeAsync() => new(_container.StartAsync());

    public ValueTask DisposeAsync() => _container.DisposeAsync();

    public string GetConnectionString(string database) =>
        $"server={_container.Hostname};port={_container.GetMappedPublicPort(3306)};"
        + $"database={database};user=root;password={Password}";

    public static string NewDatabaseName(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    // Creates a new database and applies the service's EF Core migrations to it.
    public async Task<TestDatabase<TContext>> CreateDatabaseAsync<TContext>(
        Func<DbContextOptions, TContext> createContext
    )
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseMySQL(GetConnectionString(NewDatabaseName("test")))
            .Options;

        var database = new TestDatabase<TContext>(options, createContext);
        await using var context = database.CreateContext();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return database;
    }
}

public sealed class TestDatabase<TContext>(
    DbContextOptions options,
    Func<DbContextOptions, TContext> createContext
)
    where TContext : DbContext
{
    // Use a fresh context for arrange, act and assert so results come from the database, not the change tracker.
    public TContext CreateContext() => createContext(options);
}
