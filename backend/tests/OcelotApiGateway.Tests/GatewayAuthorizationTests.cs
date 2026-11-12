using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace OcelotApiGateway.Tests;

// Runs the real gateway. The downstream service names only resolve inside the cluster, so a request
// that passes authorization comes back as 502 Bad Gateway rather than 401/403.
public class GatewayFactory : WebApplicationFactory<Program>
{
    public const string Key = "sava-gateway-test-signing-key-0123456789";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("Jwt:Key", Key);
    }

    public static string Token(string role)
    {
        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(
            handler.CreateToken(
                new SecurityTokenDescriptor
                {
                    Subject = new ClaimsIdentity([
                        new Claim(JwtRegisteredClaimNames.Name, role.ToLowerInvariant()),
                        new Claim("Role", role),
                    ]),
                    Expires = DateTime.UtcNow.AddMinutes(5),
                    SigningCredentials = new SigningCredentials(
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                        SecurityAlgorithms.HmacSha256Signature
                    ),
                }
            )
        );
    }
}

[Trait("Category", "Integration")]
public class GatewayAuthorizationTests(GatewayFactory factory) : IClassFixture<GatewayFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, string?, HttpStatusCode> Cases =>
        new()
        {
            // Anonymous routes pass straight through.
            { "POST", "/api/Account", null, HttpStatusCode.BadGateway },
            { "POST", "/api/Account/refresh", null, HttpStatusCode.BadGateway },
            // Reads: any valid token, no anonymous access.
            { "GET", "/api/Products", null, HttpStatusCode.Unauthorized },
            { "GET", "/api/Products", "User", HttpStatusCode.BadGateway },
            { "GET", "/api/Products", "Administrator", HttpStatusCode.BadGateway },
            {
                "GET",
                "/api/Orders/00000000-0000-0000-0000-000000000001",
                "User",
                HttpStatusCode.BadGateway
            },
            { "GET", "/api/Invoices", "User", HttpStatusCode.BadGateway },
            // Writes: Administrator only.
            { "POST", "/api/Products", "User", HttpStatusCode.Forbidden },
            { "POST", "/api/Products", "Administrator", HttpStatusCode.BadGateway },
            {
                "PUT",
                "/api/Customers/00000000-0000-0000-0000-000000000001",
                "User",
                HttpStatusCode.Forbidden
            },
            {
                "DELETE",
                "/api/Orders/00000000-0000-0000-0000-000000000001",
                "User",
                HttpStatusCode.Forbidden
            },
            {
                "DELETE",
                "/api/Orders/00000000-0000-0000-0000-000000000001",
                "Administrator",
                HttpStatusCode.BadGateway
            },
            {
                "POST",
                "/api/Batches/00000000-0000-0000-0000-000000000001",
                "User",
                HttpStatusCode.Forbidden
            },
            // Order status changes: Administrator only.
            {
                "POST",
                "/api/Orders/00000000-0000-0000-0000-000000000001/status",
                "User",
                HttpStatusCode.Forbidden
            },
            {
                "POST",
                "/api/Orders/00000000-0000-0000-0000-000000000001/status",
                "Administrator",
                HttpStatusCode.BadGateway
            },
            // Stock ledger: anyone can read history, only Administrator adjusts.
            {
                "GET",
                "/api/Batches/00000000-0000-0000-0000-000000000001/movements",
                "User",
                HttpStatusCode.BadGateway
            },
            {
                "GET",
                "/api/Products/00000000-0000-0000-0000-000000000001/movements",
                "User",
                HttpStatusCode.BadGateway
            },
            {
                "POST",
                "/api/Batches/00000000-0000-0000-0000-000000000001/adjustments",
                "User",
                HttpStatusCode.Forbidden
            },
            {
                "POST",
                "/api/Batches/00000000-0000-0000-0000-000000000001/adjustments",
                "Administrator",
                HttpStatusCode.BadGateway
            },
            // Reports are reads.
            { "GET", "/api/Reports/low-stock", "User", HttpStatusCode.BadGateway },
            { "GET", "/api/Reports/stock-valuation", null, HttpStatusCode.Unauthorized },
            // Account management.
            { "PUT", "/api/Account/password", "User", HttpStatusCode.BadGateway },
            { "PUT", "/api/Account/password", null, HttpStatusCode.Unauthorized },
            { "GET", "/api/Users", "User", HttpStatusCode.Forbidden },
            { "GET", "/api/Users", "Administrator", HttpStatusCode.BadGateway },
            // Unknown roles are rejected from writes.
            { "POST", "/api/Orders", "Guest", HttpStatusCode.Forbidden },
        };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Route_EnforcesExpectedPolicy(
        string method,
        string path,
        string? role,
        HttpStatusCode expected
    )
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (role != null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                GatewayFactory.Token(role)
            );
        }
        if (method is "POST" or "PUT")
        {
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        var response = await client.SendAsync(request, Ct);

        response.StatusCode.ShouldBe(expected);
    }

    [Fact]
    public async Task InvalidSignature_Returns401()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Products");
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            GatewayFactory.Token("Administrator") + "x"
        );

        (await client.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/readyz")]
    public async Task HealthEndpoints_BypassOcelot(string path)
    {
        using var client = factory.CreateClient();

        (await client.GetAsync(path, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
