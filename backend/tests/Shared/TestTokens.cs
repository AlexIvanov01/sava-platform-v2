using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Sava.Tests.Shared;

// Services only decode the bearer token the gateway forwards (they don't validate it),
// so tests can use an unsigned token that carries the user name.
internal static class TestTokens
{
    public static string Unsigned(string userName) =>
        new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Name, userName)]),
            }
        );

    public static string Bearer(string userName) => $"Bearer {Unsigned(userName)}";
}
