using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AuthenticationService.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthenticationService.DataAccess;

public class UserRepo(AuthContext context) : IUserRepo
{
    public async Task<IEnumerable<User>> GetAllUsersAsync() =>
        await context.Users.AsNoTracking().OrderBy(u => u.UserName).ToListAsync();

    public async Task<User?> GetUserAsync(Guid id) =>
        await context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);

    public async Task<User?> GetUserByNameAsync(string userName) =>
        await context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.UserName == userName);

    public async Task<bool> AnyUsersAsync() => await context.Users.AnyAsync();

    public async Task CreateUserAsync(User user)
    {
        await context.Users.AddAsync(user);
        await context.SaveChangesAsync();
    }

    public async Task<User?> UpdateUserAsync(User user)
    {
        var dbUser = await context.Users.SingleOrDefaultAsync(u => u.Id == user.Id);

        if (dbUser == null)
        {
            return null;
        }

        // Empty fields in the update are left unchanged; the user name cannot be changed.
        dbUser.Role = string.IsNullOrEmpty(user.Role) ? dbUser.Role : user.Role;
        dbUser.PasswordHash = string.IsNullOrEmpty(user.PasswordHash)
            ? dbUser.PasswordHash
            : user.PasswordHash;

        await context.SaveChangesAsync();

        return dbUser;
    }

    public async Task<User?> DeleteUserAsync(Guid id)
    {
        var dbUser = await context.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == id);

        if (dbUser == null)
        {
            return null;
        }

        // The database cascades the delete to the user's refresh tokens.
        await context.Users.Where(u => u.Id == id).ExecuteDeleteAsync();

        return dbUser;
    }
}
