using System;
using System.Linq;
using System.Threading.Tasks;
using AuthenticationService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthenticationService.DataAccess;

public class RefreshTokenRepo(AuthContext context) : IRefreshTokenRepo
{
    public async Task<RefreshToken?> GetByHashAsync(string tokenHash) =>
        await context
            .RefreshTokens.Include(t => t.User)
            .SingleOrDefaultAsync(t => t.TokenHash == tokenHash);

    public async Task AddAsync(RefreshToken token)
    {
        await context.RefreshTokens.AddAsync(token);
        await context.SaveChangesAsync();
    }

    public Task SaveChangesAsync() => context.SaveChangesAsync();

    public Task RevokeAllForUserAsync(Guid userId, DateTime now) =>
        context
            .RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now));
}
