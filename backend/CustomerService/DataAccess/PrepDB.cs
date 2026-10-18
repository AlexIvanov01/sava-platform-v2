using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CustomerService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace CustomerService.DataAccess;

public static class PrepDB
{
    public static async Task PrepPopulation(IApplicationBuilder app)
    {
        using var serviceScope = app.ApplicationServices.CreateScope();
        var env = serviceScope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var context = serviceScope.ServiceProvider.GetRequiredService<CustomerContext>();

        await RunMigrations(context);

        // Sample data is for local development only; other environments start empty.
        if (!env.IsDevelopment())
        {
            Log.Information(
                "--> Skipping seed data in the {Environment} environment.",
                env.EnvironmentName
            );
            return;
        }

        await SeedData(context, Path.Combine(env.ContentRootPath, "dummy_data.json"));
    }

    private static async Task RunMigrations(CustomerContext context)
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

    private static async Task SeedData(CustomerContext context, string seedFile)
    {
        if (await context.Customers.AnyAsync())
        {
            Log.Information("--> Data already present.");
            return;
        }

        Log.Information("--> Seeding Data.....");
        string file = await File.ReadAllTextAsync(seedFile);
        var customers = JsonSerializer.Deserialize<List<Customer>>(file) ?? [];
        context.Customers.AddRange(customers);
        await context.SaveChangesAsync();
        Log.Information("--> Seeded {Count} customers.", customers.Count);
    }
}
