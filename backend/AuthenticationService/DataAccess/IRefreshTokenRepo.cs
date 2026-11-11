using System;
using System.Threading.Tasks;
using AuthenticationService.Models;

namespace AuthenticationService.DataAccess;

public interface IRefreshTokenRepo
{
    // Includes the owning user.
    Task<RefreshToken?> GetByHashAsync(string tokenHash);
    Task AddAsync(RefreshToken token);
    Task SaveChangesAsync();
    Task RevokeAllForUserAsync(Guid userId, DateTime now);
}
