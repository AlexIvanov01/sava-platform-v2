using AuthenticationService.Models;
using Bogus.Extensions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Sava.Tests.Shared;

namespace AuthenticationService.Tests;

internal static class TestJwt
{
    public const string Key = "sava-test-signing-key-0123456789-abcdef";

    public static IConfiguration Configuration(
        string? key = Key,
        int? accessTokenMinutes = null,
        int? refreshTokenDays = null
    ) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Key"] = key,
                    ["Jwt:AccessTokenMinutes"] = accessTokenMinutes?.ToString(),
                    ["Jwt:RefreshTokenDays"] = refreshTokenDays?.ToString(),
                }
            )
            .Build();
}

// Fake users from Bogus; lengths are clamped to the model's [MaxLength] limits.
internal static class UserBuilder
{
    private static readonly PasswordHasher<User> Hasher = new();

    public static string Password() => TestData.Faker.Internet.Password(12);

    public static User User(string? role = null, string? password = null, string? userName = null)
    {
        var f = TestData.Faker;
        var user = new User
        {
            Id = f.Random.Guid(),
            UserName = userName ?? f.Internet.UserName().ClampLength(max: 100),
            Role = role ?? f.PickRandom(Models.User.Roles),
        };
        user.PasswordHash = Hasher.HashPassword(user, password ?? Password());
        return user;
    }
}
