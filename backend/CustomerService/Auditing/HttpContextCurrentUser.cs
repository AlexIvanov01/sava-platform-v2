using System;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace CustomerService.Auditing;

// Reads the user name from the bearer token the gateway forwards. The gateway has already
// validated the token, so it is only decoded here, not re-validated.
public class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    public const string SystemName = "system";
    private const string BearerPrefix = "Bearer ";

    public string Name
    {
        get
        {
            var header = httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();
            if (
                string.IsNullOrWhiteSpace(header)
                || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase)
            )
            {
                return SystemName;
            }

            var token = header[BearerPrefix.Length..].Trim();
            if (!new JsonWebTokenHandler().CanReadToken(token))
            {
                return SystemName;
            }

            var jwt = new JsonWebToken(token);
            return
                jwt.TryGetPayloadValue<string>(JwtRegisteredClaimNames.Name, out var name)
                && !string.IsNullOrWhiteSpace(name)
                ? name
                : SystemName;
        }
    }
}
