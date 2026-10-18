using System.Net;
using System.Net.Http.Json;
using JwtAuthenticationManager.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AuthenticationService.Tests;

[Trait("Category", "Integration")]
public class AccountApiTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
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
}
