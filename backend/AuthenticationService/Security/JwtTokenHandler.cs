using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AuthenticationService.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace AuthenticationService.Security;

// Issues access tokens. The gateway validates them with the same symmetric key.
public class JwtTokenHandler
{
    public const string SigningKeySetting = "Jwt:Key";
    public const string AccessTokenMinutesSetting = "Jwt:AccessTokenMinutes";
    public const string RoleClaimType = "Role";
    public const int MinimumSigningKeyLength = 32;
    public const int DefaultAccessTokenMinutes = 15;

    private readonly SigningCredentials _signingCredentials;

    public JwtTokenHandler(IConfiguration configuration)
    {
        _signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(ReadSigningKey(configuration)),
            SecurityAlgorithms.HmacSha256Signature
        );
        AccessTokenMinutes =
            configuration.GetValue<int?>(AccessTokenMinutesSetting) ?? DefaultAccessTokenMinutes;
    }

    public int AccessTokenMinutes { get; }

    // HS256 needs a key of at least 256 bits.
    public static byte[] ReadSigningKey(IConfiguration configuration)
    {
        var key = configuration[SigningKeySetting];
        if (string.IsNullOrWhiteSpace(key) || key.Length < MinimumSigningKeyLength)
            throw new InvalidOperationException(
                $"'{SigningKeySetting}' must be configured with at least {MinimumSigningKeyLength} characters."
            );
        return Encoding.UTF8.GetBytes(key);
    }

    public (string Token, int ExpiresInSeconds) CreateAccessToken(User user, DateTime now)
    {
        var expires = now.AddMinutes(AccessTokenMinutes);
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
                new List<Claim>
                {
                    new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                    new(JwtRegisteredClaimNames.Name, user.UserName),
                    new(RoleClaimType, user.Role),
                }
            ),
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = _signingCredentials,
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.WriteToken(handler.CreateToken(descriptor));

        return (token, (int)(expires - now).TotalSeconds);
    }
}
