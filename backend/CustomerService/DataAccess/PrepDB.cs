using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CustomerService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace CustomerService.DataAccess;

public static class PrepDB
{
    public static async Task PrepPopulation(IApplicationBuilder app)
    {
        using (var serviceScope = app.ApplicationServices.CreateScope())
        {
            await SeedData(serviceScope.ServiceProvider.GetRequiredService<CustomerContext>());
        }
    }

    private static async Task SeedData(CustomerContext context)
    {
        Log.Information("--> Attempting to apply migrations...");
        try
        {
            Log.Information("--> Waiting 5 seconds for DB iniialization...");
            await Task.Delay(5000);
            await context.Database.MigrateAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "--> Could not run migrations: {Ex}", ex.Message);
        }

        if (!await context.Customers.AnyAsync())
        {
            Log.Information("--> Seeding Data.....");
            string file = await File.ReadAllTextAsync("dummy_data.json");
            var people = JsonSerializer.Deserialize<List<Customer>>(file) ?? [];
            context.Customers.AddRange(people);
            await context.SaveChangesAsync();
        }
        else
        {
            Log.Information("--> Data already present.");
        }
    }
}
