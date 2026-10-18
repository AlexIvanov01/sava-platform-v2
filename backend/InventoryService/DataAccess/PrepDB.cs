using System;
using System.Collections.Generic;
using System.Linq;
using InventoryService.Models;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace InventoryService.DataAccess;

public static class PrepDB
{
    public static async Task PrepPopulation(IApplicationBuilder app)
    {
        using (var serviceScope = app.ApplicationServices.CreateScope())
        {
            await SeedData(serviceScope.ServiceProvider.GetRequiredService<ProductContext>());
        }
    }

    private static async Task SeedData(ProductContext context)
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

        if (!await context.Products.AnyAsync())
        {
            Log.Information("--> Seeding data...");
            string file = await System.IO.File.ReadAllTextAsync("dummy_data.json");
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
