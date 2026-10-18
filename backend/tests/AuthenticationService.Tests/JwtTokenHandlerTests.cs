using System.IdentityModel.Tokens.Jwt;
using System.Text;
using JwtAuthenticationManager;
using JwtAuthenticationManager.Models;
using Microsoft.IdentityModel.Tokens;

namespace AuthenticationService.Tests;

public class JwtTokenHandlerTests
{
    private readonly JwtTokenHandler _handler = new();

    [Theory]
    [InlineData("admin", "admin123", "Administrator")]
    [InlineData("user", "user123", "User")]
    public void GenerateJwtToken_ValidCredentials_ReturnsTokenWithUserClaims(
        string userName,
        string password,
        string role
    )
    {
        var response = _handler.GenerateJwtToken(
            new AuthenticationRequest { UserName = userName, Password = password }
        );

        response.ShouldNotBeNull();
        response.UserName.ShouldBe(userName);
        response.ExpiresIn.ShouldBeInRange(
            JwtTokenHandler.JWT_TOKEN_VALIDITY_MINS * 60 - 5,
            JwtTokenHandler.JWT_TOKEN_VALIDITY_MINS * 60
        );

        var token = new JwtSecurityTokenHandler().ReadJwtToken(response.JwtToken);
        token.Claims.ShouldContain(c =>
            c.Type == JwtRegisteredClaimNames.Name && c.Value == userName
        );
        token.Claims.ShouldContain(c => c.Type == "Role" && c.Value == role);
    }

    [Fact]
    public async Task GenerateJwtToken_ValidCredentials_TokenIsSignedWithSharedKey()
    {
        var response = _handler.GenerateJwtToken(
            new AuthenticationRequest { UserName = "admin", Password = "admin123" }
        );

        var result = await new JwtSecurityTokenHandler().ValidateTokenAsync(
            response!.JwtToken,
            new TokenValidationParameters
            {
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.ASCII.GetBytes(JwtTokenHandler.JWT_SECURITY_KEY)
                ),
                ValidateIssuer = false,
                ValidateAudience = false,
            }
        );

        result.IsValid.ShouldBeTrue(result.Exception?.Message);
    }

    [Theory]
    [InlineData("admin", "wrong")]
    [InlineData("unknown", "admin123")]
    [InlineData("ADMIN", "admin123")]
    [InlineData("", "admin123")]
    [InlineData("admin", "")]
    [InlineData("   ", "   ")]
    [InlineData(null, "admin123")]
    [InlineData("admin", null)]
    public void GenerateJwtToken_InvalidCredentials_ReturnsNull(string? userName, string? password)
    {
        var response = _handler.GenerateJwtToken(
            new AuthenticationRequest { UserName = userName, Password = password }
        );

        response.ShouldBeNull();
    }
}
