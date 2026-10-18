using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using JwtAuthenticationManager.Models;
using Microsoft.IdentityModel.Tokens;

namespace AuthenticationService.Tests;

[Trait("Category", "Integration")]
public class AccountApiTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task PostAccount_ValidCredentials_Returns200WithToken()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/account",
            new { userName = "admin", password = "admin123" },
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<AuthenticationResponse>(
            TestContext.Current.CancellationToken
        );
        body.ShouldNotBeNull();
        body.UserName.ShouldBe("admin");
        body.JwtToken.ShouldNotBeNullOrWhiteSpace();

        var validation = await new JwtSecurityTokenHandler().ValidateTokenAsync(
            body.JwtToken,
            new TokenValidationParameters
            {
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwt.Key)),
                ValidateIssuer = false,
                ValidateAudience = false,
            }
        );
        validation.IsValid.ShouldBeTrue(validation.Exception?.Message);
    }

    [Fact]
    public async Task PostAccount_WrongPassword_Returns401()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/account",
            new { userName = "admin", password = "wrong" },
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostAccount_MissingPassword_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/account",
            new { userName = "admin" },
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/readyz")]
    public async Task HealthEndpoints_Return200(string path)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

[Trait("Category", "Integration")]
public class MissingSigningKeyTests(MissingKeyAuthApiFactory factory)
    : IClassFixture<MissingKeyAuthApiFactory>
{
    [Fact]
    public void Startup_WithoutSigningKey_Fails()
    {
        var exception = Should.Throw<InvalidOperationException>(() => factory.CreateClient());

        exception.Message.ShouldContain("Jwt:Key");
    }
}
