using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AuthenticationService.Dtos;
using AuthenticationService.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Sava.Tests.Shared;

namespace AuthenticationService.Tests;

[Trait("Category", "Integration")]
public class AccountApiTests(AuthApiFactory factory) : IClassFixture<AuthApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_SeededAdmin_ReturnsSignedTokenPair()
    {
        using var client = factory.CreateClient();

        var tokens = await LoginAsync(
            client,
            AuthApiFactory.AdminUserName,
            AuthApiFactory.AdminPassword
        );

        tokens.UserName.ShouldBe(AuthApiFactory.AdminUserName);
        tokens.Role.ShouldBe(User.AdministratorRole);
        tokens.AccessTokenExpiresIn.ShouldBe(15 * 60);
        tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        tokens.RefreshTokenExpiresIn.ShouldBe(7 * 24 * 3600);
        var validation = await new JwtSecurityTokenHandler().ValidateTokenAsync(
            tokens.AccessToken,
            new TokenValidationParameters
            {
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwt.Key)),
                ValidateIssuer = false,
                ValidateAudience = false,
            }
        );
        validation.IsValid.ShouldBeTrue(validation.Exception?.Message);
    }

    [Theory]
    [InlineData("admin", "wrong")]
    [InlineData("nobody", "whatever1")]
    public async Task Login_InvalidCredentials_Returns401(string userName, string password)
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/account",
            new LoginRequestDto(userName, password),
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_MissingPassword_Returns400()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/account", new { userName = "admin" }, Ct);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Refresh_RotatesAndRejectsReuse_LogoutRevokes()
    {
        using var client = factory.CreateClient();
        var first = await LoginAsync(
            client,
            AuthApiFactory.AdminUserName,
            AuthApiFactory.AdminPassword
        );

        var refresh = await client.PostAsJsonAsync(
            "/api/account/refresh",
            new RefreshRequestDto(first.RefreshToken),
            Ct
        );
        refresh.StatusCode.ShouldBe(HttpStatusCode.OK);
        var second = (
            await refresh.Content.ReadFromJsonAsync<TokenResponseDto>(Ct)
        ).ShouldNotBeNull();
        second.RefreshToken.ShouldNotBe(first.RefreshToken);
        second.UserName.ShouldBe(AuthApiFactory.AdminUserName);

        (
            await client.PostAsJsonAsync(
                "/api/account/refresh",
                new RefreshRequestDto(first.RefreshToken),
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var logout = await client.PostAsJsonAsync(
            "/api/account/logout",
            new RefreshRequestDto(second.RefreshToken),
            Ct
        );
        logout.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (
            await client.PostAsJsonAsync(
                "/api/account/refresh",
                new RefreshRequestDto(second.RefreshToken),
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_UnknownToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/account/refresh",
            new RefreshRequestDto("garbage"),
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UserLifecycle_AdminCreatesUser_UserChangesPassword()
    {
        using var admin = await LoggedInClientAsync(
            AuthApiFactory.AdminUserName,
            AuthApiFactory.AdminPassword
        );
        var create = UserBuilder.User(role: User.UserRole);
        var password = UserBuilder.Password();

        var created = await admin.PostAsJsonAsync(
            "/api/users",
            new UserCreateDto(create.UserName, password, User.UserRole),
            Ct
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var user = (await created.Content.ReadFromJsonAsync<UserReadDto>(Ct)).ShouldNotBeNull();
        created
            .Headers.Location.ShouldNotBeNull()
            .ToString()
            .ShouldEndWith($"/api/users/{user.Id}", Case.Insensitive);
        user.UserName.ShouldBe(create.UserName);
        user.Role.ShouldBe(User.UserRole);
        user.Audit.CreatedBy.ShouldBe(AuthApiFactory.AdminUserName);

        (
            await admin.PostAsJsonAsync(
                "/api/users",
                new UserCreateDto(create.UserName, password, User.UserRole),
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.Conflict);

        using var member = await LoggedInClientAsync(create.UserName, password);
        (await member.GetAsync("/api/users", Ct)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        var newPassword = UserBuilder.Password();
        var wrongCurrent = await member.PutAsJsonAsync(
            "/api/account/password",
            new ChangePasswordDto("not-it-" + password, newPassword),
            Ct
        );
        wrongCurrent.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var change = await member.PutAsJsonAsync(
            "/api/account/password",
            new ChangePasswordDto(password, newPassword),
            Ct
        );
        change.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        using (var anonymous = factory.CreateClient())
        {
            (
                await anonymous.PostAsJsonAsync(
                    "/api/account",
                    new LoginRequestDto(create.UserName, password),
                    Ct
                )
            ).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
            await LoginAsync(anonymous, create.UserName, newPassword);
        }

        var promote = await admin.PutAsJsonAsync(
            $"/api/users/{user.Id}",
            new UserUpdateDto(User.AdministratorRole, null),
            Ct
        );
        promote.StatusCode.ShouldBe(HttpStatusCode.OK);
        var promoted = (await promote.Content.ReadFromJsonAsync<UserReadDto>(Ct)).ShouldNotBeNull();
        promoted.Role.ShouldBe(User.AdministratorRole);
        promoted.Audit.UpdatedBy.ShouldBe(AuthApiFactory.AdminUserName);

        var list = await admin.GetFromJsonAsync<List<UserReadDto>>("/api/users", Ct);
        list.ShouldNotBeNull().ShouldContain(u => u.Id == user.Id);

        (await admin.DeleteAsync($"/api/users/{user.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );
        (await admin.GetAsync($"/api/users/{user.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );
    }

    [Fact]
    public async Task Users_AdminCannotDeleteOrDemoteThemselves()
    {
        using var admin = await LoggedInClientAsync(
            AuthApiFactory.AdminUserName,
            AuthApiFactory.AdminPassword
        );
        var me = (await admin.GetFromJsonAsync<List<UserReadDto>>("/api/users", Ct))
            .ShouldNotBeNull()
            .Single(u => u.UserName == AuthApiFactory.AdminUserName);

        var demote = await admin.PutAsJsonAsync(
            $"/api/users/{me.Id}",
            new UserUpdateDto(User.UserRole, null),
            Ct
        );
        demote.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await demote.Content.ReadFromJsonAsync<ProblemDetails>(Ct))
            .ShouldNotBeNull()
            .Title.ShouldBe("Cannot demote yourself");

        var delete = await admin.DeleteAsync($"/api/users/{me.Id}", Ct);
        delete.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await delete.Content.ReadFromJsonAsync<ProblemDetails>(Ct))
            .ShouldNotBeNull()
            .Title.ShouldBe("Cannot delete yourself");
    }

    [Theory]
    [InlineData("GET", "/api/users")]
    [InlineData("PUT", "/api/account/password")]
    public async Task ProtectedEndpoints_WithoutToken_Return401(string method, string path)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = JsonContent.Create(new { }),
        };

        (await client.SendAsync(request, Ct)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateUser_InvalidRoleOrShortPassword_Returns400()
    {
        using var admin = await LoggedInClientAsync(
            AuthApiFactory.AdminUserName,
            AuthApiFactory.AdminPassword
        );

        (
            await admin.PostAsJsonAsync(
                "/api/users",
                new UserCreateDto("someone", UserBuilder.Password(), "Root"),
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (
            await admin.PostAsJsonAsync(
                "/api/users",
                new UserCreateDto("someone", "short", User.UserRole),
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/readyz")]
    public async Task HealthEndpoints_Return200(string path)
    {
        using var client = factory.CreateClient();

        (await client.GetAsync(path, Ct)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<HttpClient> LoggedInClientAsync(string userName, string password)
    {
        var client = factory.CreateClient();
        var tokens = await LoginAsync(client, userName, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            tokens.AccessToken
        );
        return client;
    }

    private static async Task<TokenResponseDto> LoginAsync(
        HttpClient client,
        string userName,
        string password
    )
    {
        var response = await client.PostAsJsonAsync(
            "/api/account",
            new LoginRequestDto(userName, password),
            Ct
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TokenResponseDto>(Ct)).ShouldNotBeNull();
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
