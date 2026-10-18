using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace AuthenticationService.Tests;

// Production skips appsettings.Development.json, so the signing key comes only from SigningKey.
public class AuthApiFactory : WebApplicationFactory<Program>
{
    protected virtual string? SigningKey => TestJwt.Key;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("Jwt:Key", SigningKey);
    }
}

public sealed class MissingKeyAuthApiFactory : AuthApiFactory
{
    protected override string? SigningKey => null;
}
