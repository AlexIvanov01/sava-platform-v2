using AuthenticationService.DataAccess;
using AuthenticationService.Models;
using AuthenticationService.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Sava.Tests.Shared;

namespace AuthenticationService.Tests;

[Trait("Category", "Integration")]
public class TokenServiceTests(MySqlFixture mySql) : IAsyncLifetime
{
    private TestDatabase<AuthContext> _db = null!;
    private readonly FakeTimeProvider _clock = new(
        new DateTimeOffset(TestData.Date(2026, 2026), TimeSpan.Zero)
    );
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        _db = await mySql.CreateDatabaseAsync(options => new AuthContext(options));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task Migrations_MatchTheModel()
    {
        await using var context = _db.CreateContext();

        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task IssueAsync_StoresOnlyTheHashOfTheRefreshToken()
    {
        var user = await SeedUserAsync();

        var response = await CreateService().IssueAsync(user);

        response.UserName.ShouldBe(user.UserName);
        response.Role.ShouldBe(user.Role);
        response.ExpiresIn.ShouldBe(JwtTokenHandler.DefaultAccessTokenMinutes * 60);
        response.RefreshTokenExpiresIn.ShouldBe(TokenService.DefaultRefreshTokenDays * 24 * 3600);
        await using var context = _db.CreateContext();
        var stored = await context.RefreshTokens.SingleAsync(Ct);
        stored.UserId.ShouldBe(user.Id);
        stored.TokenHash.ShouldBe(TokenService.Hash(response.RefreshToken));
        stored.TokenHash.ShouldNotBe(response.RefreshToken);
        stored.ExpiresAt.ShouldBe(_clock.GetUtcNow().UtcDateTime.AddDays(7));
        stored.RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task RefreshAsync_RotatesTheToken()
    {
        var user = await SeedUserAsync();
        var first = await CreateService().IssueAsync(user);
        _clock.Advance(TimeSpan.FromMinutes(30));

        var second = await CreateService().RefreshAsync(first.RefreshToken);

        second.ShouldNotBeNull();
        second.RefreshToken.ShouldNotBe(first.RefreshToken);
        second.JwtToken.ShouldNotBe(first.JwtToken);
        await using var context = _db.CreateContext();
        var tokens = await context.RefreshTokens.OrderBy(t => t.CreatedAt).ToListAsync(Ct);
        tokens.Count.ShouldBe(2);
        tokens[0].RevokedAt.ShouldBe(_clock.GetUtcNow().UtcDateTime);
        tokens[0].ReplacedByTokenHash.ShouldBe(tokens[1].TokenHash);
        tokens[1].RevokedAt.ShouldBeNull();
    }

    [Fact]
    public async Task RefreshAsync_UnknownToken_ReturnsNull()
    {
        (await CreateService().RefreshAsync("not-a-token")).ShouldBeNull();
    }

    [Fact]
    public async Task RefreshAsync_ExpiredToken_ReturnsNull()
    {
        var user = await SeedUserAsync();
        var issued = await CreateService().IssueAsync(user);
        _clock.Advance(TimeSpan.FromDays(TokenService.DefaultRefreshTokenDays + 1));

        (await CreateService().RefreshAsync(issued.RefreshToken)).ShouldBeNull();
    }

    [Fact]
    public async Task RefreshAsync_ReusedRotatedToken_RevokesWholeChain()
    {
        var user = await SeedUserAsync();
        var first = await CreateService().IssueAsync(user);
        var second = (await CreateService().RefreshAsync(first.RefreshToken)).ShouldNotBeNull();

        (await CreateService().RefreshAsync(first.RefreshToken)).ShouldBeNull();

        // The replay burned the still-valid successor too.
        (await CreateService().RefreshAsync(second.RefreshToken)).ShouldBeNull();
        await using var context = _db.CreateContext();
        (await context.RefreshTokens.CountAsync(t => t.RevokedAt == null, Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task RevokeAsync_DeactivatesTokenOnce()
    {
        var user = await SeedUserAsync();
        var issued = await CreateService().IssueAsync(user);

        (await CreateService().RevokeAsync(issued.RefreshToken)).ShouldBeTrue();
        (await CreateService().RevokeAsync(issued.RefreshToken)).ShouldBeFalse();
        (await CreateService().RefreshAsync(issued.RefreshToken)).ShouldBeNull();
    }

    [Fact]
    public async Task DeletingUser_CascadesToRefreshTokens()
    {
        var user = await SeedUserAsync();
        await CreateService().IssueAsync(user);

        await using (var context = _db.CreateContext())
        {
            (await new UserRepo(context).DeleteUserAsync(user.Id)).ShouldNotBeNull();
        }

        await using var assertContext = _db.CreateContext();
        (await assertContext.RefreshTokens.AnyAsync(Ct)).ShouldBeFalse();
    }

    // Each call uses a fresh context so results come from the database.
    private TokenService CreateService() =>
        new(
            new JwtTokenHandler(TestJwt.Configuration()),
            new RefreshTokenRepo(_db.CreateContext()),
            TestJwt.Configuration(),
            _clock
        );

    private async Task<User> SeedUserAsync()
    {
        var user = UserBuilder.User();
        await using var context = _db.CreateContext();
        await new UserRepo(context).CreateUserAsync(user);
        return user;
    }
}
