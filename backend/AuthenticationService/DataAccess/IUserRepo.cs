using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AuthenticationService.Models;

namespace AuthenticationService.DataAccess;

public interface IUserRepo
{
    Task<IEnumerable<User>> GetAllUsersAsync();
    Task<User?> GetUserAsync(Guid id);
    Task<User?> GetUserByNameAsync(string userName);
    Task<bool> AnyUsersAsync();
    Task CreateUserAsync(User user);
    Task<User?> UpdateUserAsync(User user);
    Task<User?> DeleteUserAsync(Guid id);
}
