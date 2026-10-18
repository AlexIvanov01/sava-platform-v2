using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using InventoryService.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace InventoryService.DataAccess;

public static class PrepDB
{
    public static async Task PrepPopulation(IApplicationBuilder app)
    {
        using (var serviceScope = app.ApplicationServices.CreateScope())
        {
            var env = serviceScope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
            await SeedData(
                serviceScope.ServiceProvider.GetRequiredService<ProductContext>(),
                Path.Combine(env.ContentRootPath, "dummy_data.json")
            );
        }
    }

    private static async Task SeedData(ProductContext context, string seedFile)
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

        if (!await context.Products.AnyAsync())
        {
            Log.Information("--> Seeding data...");
            string file = await File.ReadAllTextAsync(seedFile);
            var people = JsonSerializer.Deserialize<List<Product>>(file) ?? [];
            context.Products.AddRange(people);
            await context.SaveChangesAsync();
        }
        else
        {
            Log.Information("--> Data is already present");
        }
    }
}
