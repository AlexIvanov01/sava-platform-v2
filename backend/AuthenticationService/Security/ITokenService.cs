using System.Threading.Tasks;
using AuthenticationService.Dtos;
using AuthenticationService.Models;

namespace AuthenticationService.Security;

public interface ITokenService
{
    // Access token plus a new refresh token for the user.
    Task<TokenResponseDto> IssueAsync(User user);

    // Rotates the refresh token: the presented one is revoked and a new pair is issued.
    // Returns null when the token is unknown, expired or already revoked.
    Task<TokenResponseDto?> RefreshAsync(string rawRefreshToken);

    // Returns false when the token is unknown or already inactive.
    Task<bool> RevokeAsync(string rawRefreshToken);
}
