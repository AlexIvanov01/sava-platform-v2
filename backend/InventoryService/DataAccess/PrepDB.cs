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
using Microsoft.Extensions.Hosting;
using Serilog;

namespace InventoryService.DataAccess;

public static class PrepDB
{
    public static async Task PrepPopulation(IApplicationBuilder app)
    {
        using var serviceScope = app.ApplicationServices.CreateScope();
        var env = serviceScope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var context = serviceScope.ServiceProvider.GetRequiredService<ProductContext>();

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

        await SeedData(
            context,
            Path.Combine(env.ContentRootPath, "dummy_categories.json"),
            Path.Combine(env.ContentRootPath, "dummy_data.json")
        );
    }

    private static async Task RunMigrations(ProductContext context)
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

    private static async Task SeedData(
        ProductContext context,
        string categoriesFile,
        string productsFile
    )
    {
        if (await context.Products.AnyAsync() || await context.Categories.AnyAsync())
        {
            Log.Information("--> Data is already present");
            return;
        }

        Log.Information("--> Seeding data...");
        // Products reference categories by CategoryId, so both go in one SaveChanges.
        var categories =
            JsonSerializer.Deserialize<List<Category>>(await File.ReadAllTextAsync(categoriesFile))
            ?? [];
        context.Categories.AddRange(categories);

        string file = await File.ReadAllTextAsync(productsFile);
        var products = JsonSerializer.Deserialize<List<Product>>(file) ?? [];
        foreach (var product in products)
        {
            // ProductBatch.Product defaults to a new, empty product, which EF would also insert.
            foreach (var batch in product.Batches)
            {
                batch.Product = product;
                batch.ProductId = product.Id;

                // Seeded stock enters the ledger as a receipt on the day the batch was added.
                var received = batch.Quantity ?? 0;
                batch.Quantity = 0;
                if (received != 0)
                {
                    batch.Move(StockMovementType.Receipt, received, note: "Seed data").CreatedAt =
                        batch.CreatedAt;
                }
            }
        }
        context.Products.AddRange(products);
        await context.SaveChangesAsync();
        Log.Information(
            "--> Seeded {Categories} categories and {Count} products with {Batches} batches.",
            categories.Count,
            products.Count,
            products.Sum(p => p.Batches.Count)
        );
    }
}
