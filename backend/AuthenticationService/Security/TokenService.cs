using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using AuthenticationService.DataAccess;
using AuthenticationService.Dtos;
using AuthenticationService.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Serilog;

namespace AuthenticationService.Security;

public class TokenService(
    JwtTokenHandler jwtTokenHandler,
    IRefreshTokenRepo refreshTokens,
    IConfiguration configuration,
    TimeProvider timeProvider
) : ITokenService
{
    public const string RefreshTokenDaysSetting = "Jwt:RefreshTokenDays";
    public const int DefaultRefreshTokenDays = 7;
    private const int RefreshTokenBytes = 64;

    private readonly int _refreshTokenDays =
        configuration.GetValue<int?>(RefreshTokenDaysSetting) ?? DefaultRefreshTokenDays;

    public async Task<TokenResponseDto> IssueAsync(User user)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var (rawRefreshToken, refreshToken) = NewRefreshToken(user.Id, now);
        await refreshTokens.AddAsync(refreshToken);

        return BuildResponse(user, now, rawRefreshToken, refreshToken);
    }

    public async Task<TokenResponseDto?> RefreshAsync(string rawRefreshToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var current = await refreshTokens.GetByHashAsync(Hash(rawRefreshToken));

        if (current == null)
        {
            return null;
        }

        if (!current.IsActive(now))
        {
            // Presenting a rotated-out token means it leaked or was replayed; cut off the whole chain.
            if (current.RevokedAt != null && current.ReplacedByTokenHash != null)
            {
                Log.Warning(
                    "--> Reuse of a rotated refresh token for user {UserId}; revoking all of their tokens.",
                    current.UserId
                );
                await refreshTokens.RevokeAllForUserAsync(current.UserId, now);
            }
            return null;
        }

        var (rawNext, next) = NewRefreshToken(current.UserId, now);
        current.RevokedAt = now;
        current.ReplacedByTokenHash = next.TokenHash;
        await refreshTokens.AddAsync(next);

        return BuildResponse(current.User, now, rawNext, next);
    }

    public async Task<bool> RevokeAsync(string rawRefreshToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var token = await refreshTokens.GetByHashAsync(Hash(rawRefreshToken));

        if (token == null || !token.IsActive(now))
        {
            return false;
        }

        token.RevokedAt = now;
        await refreshTokens.SaveChangesAsync();
        return true;
    }

    public static string Hash(string rawToken) =>
        Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken)));

    private (string Raw, RefreshToken Token) NewRefreshToken(Guid userId, DateTime now)
    {
        var raw = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(RefreshTokenBytes));
        var token = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = Hash(raw),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_refreshTokenDays),
        };
        return (raw, token);
    }

    private TokenResponseDto BuildResponse(
        User user,
        DateTime now,
        string rawRefreshToken,
        RefreshToken refreshToken
    )
    {
        var (accessToken, expiresIn) = jwtTokenHandler.CreateAccessToken(user, now);
        return new TokenResponseDto(
            user.UserName,
            user.Role,
            accessToken,
            expiresIn,
            rawRefreshToken,
            (int)(refreshToken.ExpiresAt - now).TotalSeconds
        );
    }
}
