using AuthenticationService.DataAccess;
using AuthenticationService.Models;
using Microsoft.EntityFrameworkCore;
using Sava.Tests.Shared;

namespace AuthenticationService.Tests;

[Trait("Category", "Integration")]
public class UserRepoTests(MySqlFixture mySql) : IAsyncLifetime
{
    private TestDatabase<AuthContext> _db = null!;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        _db = await mySql.CreateDatabaseAsync(options => new AuthContext(options));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task CreateUserAsync_PersistsAndStampsAudit()
    {
        var user = UserBuilder.User();

        await using (var context = _db.CreateContext())
        {
            await new UserRepo(context).CreateUserAsync(user);
        }

        await using var assertContext = _db.CreateContext();
        var saved = await new UserRepo(assertContext).GetUserByNameAsync(user.UserName);
        saved.ShouldNotBeNull();
        saved.Id.ShouldBe(user.Id);
        saved.Role.ShouldBe(user.Role);
        saved.PasswordHash.ShouldBe(user.PasswordHash);
        saved.CreatedAt.ShouldNotBeNull();
        (await new UserRepo(assertContext).AnyUsersAsync()).ShouldBeTrue();
    }

    [Fact]
    public async Task CreateUserAsync_DuplicateUserName_Throws()
    {
        var name = UserBuilder.User().UserName;
        await using var context = _db.CreateContext();
        await new UserRepo(context).CreateUserAsync(UserBuilder.User(userName: name));

        await Should.ThrowAsync<DbUpdateException>(() =>
            new UserRepo(_db.CreateContext()).CreateUserAsync(UserBuilder.User(userName: name))
        );
    }

    [Fact]
    public async Task UpdateUserAsync_OnlyOverwritesSuppliedFields()
    {
        var user = UserBuilder.User(role: User.UserRole);
        await using (var context = _db.CreateContext())
        {
            await new UserRepo(context).CreateUserAsync(user);
        }

        await using (var context = _db.CreateContext())
        {
            var result = await new UserRepo(context).UpdateUserAsync(
                new User { Id = user.Id, Role = User.AdministratorRole }
            );
            result.ShouldNotBeNull().Role.ShouldBe(User.AdministratorRole);
            result.PasswordHash.ShouldBe(user.PasswordHash);
            result.UserName.ShouldBe(user.UserName);
        }

        await using var assertContext = _db.CreateContext();
        var saved = await assertContext.Users.SingleAsync(Ct);
        saved.Role.ShouldBe(User.AdministratorRole);
        saved.PasswordHash.ShouldBe(user.PasswordHash);
        saved.UpdatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task UpdateAndDelete_Missing_ReturnNull()
    {
        await using var context = _db.CreateContext();
        var repo = new UserRepo(context);
        var id = TestData.Faker.Random.Guid();

        (await repo.UpdateUserAsync(new User { Id = id })).ShouldBeNull();
        (await repo.DeleteUserAsync(id)).ShouldBeNull();
        (await repo.GetUserAsync(id)).ShouldBeNull();
    }

    [Fact]
    public async Task GetAllUsersAsync_OrdersByUserName()
    {
        var users = Enumerable.Range(0, 3).Select(_ => UserBuilder.User()).ToList();
        await using (var context = _db.CreateContext())
        {
            context.Users.AddRange(users);
            await context.SaveChangesAsync(Ct);
        }

        await using var assertContext = _db.CreateContext();
        var all = (await new UserRepo(assertContext).GetAllUsersAsync()).ToList();

        all.Select(u => u.UserName)
            .ShouldBe(users.Select(u => u.UserName).Order(StringComparer.Ordinal));
    }
}
