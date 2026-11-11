using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AuthenticationService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AuthenticationService.DataAccess;

public static class PrepDB
{
    public const string AdminUserNameSetting = "Seed:AdminUserName";
    public const string AdminPasswordSetting = "Seed:AdminPassword";
    public const string DefaultAdminUserName = "admin";

    // Plain-text passwords only exist in the Development seed file.
    private sealed record SeedUser(string UserName, string Password, string Role);

    public static async Task PrepPopulation(IApplicationBuilder app)
    {
        using var serviceScope = app.ApplicationServices.CreateScope();
        var env = serviceScope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var configuration = serviceScope.ServiceProvider.GetRequiredService<IConfiguration>();
        var context = serviceScope.ServiceProvider.GetRequiredService<AuthContext>();
        var hasher = serviceScope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();

        await RunMigrations(context);

        if (await context.Users.AnyAsync())
        {
            Log.Information("--> Users already present.");
            return;
        }

        // The first administrator comes from configuration in every environment (a Secret in the cluster).
        await SeedAdmin(context, hasher, configuration);

        if (env.IsDevelopment())
        {
            await SeedData(context, hasher, Path.Combine(env.ContentRootPath, "dummy_data.json"));
        }
    }

    private static async Task RunMigrations(AuthContext context)
    {
        Log.Information("--> Attempting to apply migrations...");
        try
        {
            Log.Information("--> Waiting 5 seconds for DB initialization...");
            await Task.Delay(5000);
            await context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "--> Could not run migrations: {Ex}", ex.Message);
        }
    }

    private static async Task SeedAdmin(
        AuthContext context,
        IPasswordHasher<User> hasher,
        IConfiguration configuration
    )
    {
        var password = configuration[AdminPasswordSetting];
        if (string.IsNullOrWhiteSpace(password))
        {
            Log.Error(
                "--> No users exist and '{Setting}' is not configured; nobody can log in until it is set.",
                AdminPasswordSetting
            );
            return;
        }

        var admin = new User
        {
            Id = Guid.NewGuid(),
            UserName = configuration[AdminUserNameSetting] ?? DefaultAdminUserName,
            Role = User.AdministratorRole,
        };
        admin.PasswordHash = hasher.HashPassword(admin, password);
        context.Users.Add(admin);
        await context.SaveChangesAsync();
        Log.Information("--> Seeded administrator {UserName}.", admin.UserName);
    }

    private static async Task SeedData(
        AuthContext context,
        IPasswordHasher<User> hasher,
        string seedFile
    )
    {
        Log.Information("--> Seeding Data.....");
        string file = await File.ReadAllTextAsync(seedFile);
        var seeds = JsonSerializer.Deserialize<List<SeedUser>>(file) ?? [];
        var count = 0;

        foreach (var seed in seeds)
        {
            if (await context.Users.AnyAsync(u => u.UserName == seed.UserName))
            {
                continue;
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                UserName = seed.UserName,
                Role = seed.Role,
            };
            user.PasswordHash = hasher.HashPassword(user, seed.Password);
            context.Users.Add(user);
            count++;
        }

        await context.SaveChangesAsync();
        Log.Information("--> Seeded {Count} users.", count);
    }
}
