using Microsoft.Extensions.Configuration;

namespace AuthenticationService.Tests;

internal static class TestJwt
{
    public const string Key = "sava-test-signing-key-0123456789-abcdef";

    public static IConfiguration Configuration(string? key = Key) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = key })
            .Build();
}
