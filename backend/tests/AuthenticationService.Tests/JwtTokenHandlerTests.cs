using System.IdentityModel.Tokens.Jwt;
using System.Text;
using AuthenticationService.Security;
using Microsoft.IdentityModel.Tokens;
using Sava.Tests.Shared;

namespace AuthenticationService.Tests;

public class JwtTokenHandlerTests
{
    private static readonly DateTime Now = DateTime.SpecifyKind(
        TestData.Date(2026, 2026),
        DateTimeKind.Utc
    );

    [Fact]
    public void CreateAccessToken_ContainsUserClaimsAndExpiresAfterConfiguredMinutes()
    {
        var handler = new JwtTokenHandler(TestJwt.Configuration(accessTokenMinutes: 20));
        var user = UserBuilder.User();

        var (token, expiresIn) = handler.CreateAccessToken(user, Now);

        expiresIn.ShouldBe(20 * 60);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.ShouldContain(c =>
            c.Type == JwtRegisteredClaimNames.Name && c.Value == user.UserName
        );
        jwt.Claims.ShouldContain(c =>
            c.Type == JwtRegisteredClaimNames.Sub && c.Value == user.Id.ToString()
        );
        jwt.Claims.ShouldContain(c =>
            c.Type == JwtTokenHandler.RoleClaimType && c.Value == user.Role
        );
        jwt.ValidTo.ShouldBe(Now.AddMinutes(20), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void AccessTokenMinutes_DefaultsTo15()
    {
        new JwtTokenHandler(TestJwt.Configuration()).AccessTokenMinutes.ShouldBe(
            JwtTokenHandler.DefaultAccessTokenMinutes
        );
        JwtTokenHandler.DefaultAccessTokenMinutes.ShouldBe(15);
    }

    [Fact]
    public async Task CreateAccessToken_IsSignedWithConfiguredKey()
    {
        var handler = new JwtTokenHandler(TestJwt.Configuration());

        var (token, _) = handler.CreateAccessToken(UserBuilder.User(), DateTime.UtcNow);

        var result = await new JwtSecurityTokenHandler().ValidateTokenAsync(
            token,
            new TokenValidationParameters
            {
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwt.Key)),
                ValidateIssuer = false,
                ValidateAudience = false,
            }
        );
        result.IsValid.ShouldBeTrue(result.Exception?.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short-key")]
    public void Constructor_MissingOrShortSigningKey_Throws(string? key)
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            new JwtTokenHandler(TestJwt.Configuration(key))
        );

        exception.Message.ShouldContain(JwtTokenHandler.SigningKeySetting);
    }
}
