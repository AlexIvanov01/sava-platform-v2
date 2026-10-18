using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderService.Models;
using OrderService.SyncDataServices.Grpc;
using Serilog;

namespace OrderService.DataAccess;

public static class PrepDB
{
    public static async Task PrepPopulation(IApplicationBuilder app)
    {
        using var serviceScope = app.ApplicationServices.CreateScope();
        var env = serviceScope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
        var context = serviceScope.ServiceProvider.GetRequiredService<OrderContext>();
        await RunMigrations(context);

        var grpcInventoryClient =
            serviceScope.ServiceProvider.GetRequiredService<IInventoryDataClient>();
        var grpcCustomerClient =
            serviceScope.ServiceProvider.GetRequiredService<ICustomerDataClient>();

        Log.Information(
            "--> Waiting 5 seconds for Inventory and Customer Service initialization..."
        );
        await Task.Delay(5000);

        var items = grpcInventoryClient.ReturnAllItems();
        var customers = grpcCustomerClient.ReturnAllCustomers();

        if (items != null)
        {
            await SyncItems(serviceScope.ServiceProvider.GetRequiredService<IItemRepo>(), items);
        }
        if (customers != null)
        {
            await SyncCustomers(
                serviceScope.ServiceProvider.GetRequiredService<ICustomerRepo>(),
                customers
            );
        }

        // Sample data is for local development only; other environments start empty.
        if (!env.IsDevelopment())
        {
            Log.Information(
                "--> Skipping seed data in the {Environment} environment.",
                env.EnvironmentName
            );
            return;
        }

        await SeedOrdersAsync(context, Path.Combine(env.ContentRootPath, "dummy_data.json"));
    }

    public static async Task SyncCustomers(
        ICustomerRepo customerRepo,
        IEnumerable<Customer> customers
    )
    {
        Log.Information("--> Syncing customers...");
        await customerRepo.SyncCustomersAsync(customers);
        Log.Information("--> Customers sync complete.");
    }

    public static async Task SyncItems(IItemRepo itemRepo, IEnumerable<Item> items)
    {
        try
        {
            Log.Information("--> Syncing items...");
            await itemRepo.SyncItemsAsync(items);
            Log.Information("--> Items sync complete.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "--> An error occurred while syncing: {Ex}", ex.Message);
        }
    }

    // Seeds orders and invoices that reference the customers and batches seeded by the Customer
    // and Inventory services, so it only runs once both have been synced. Invoice totals are
    // computed from the synced item prices.
    public static async Task SeedOrdersAsync(OrderContext context, string seedFile)
    {
        if (await context.Orders.AnyAsync())
        {
            Log.Information("--> Orders already present.");
            return;
        }

        string file = await File.ReadAllTextAsync(seedFile);
        var seeds = JsonSerializer.Deserialize<List<OrderSeed>>(file) ?? [];
        var customerIds = seeds.Select(s => s.CustomerId).ToHashSet();
        var itemIds = seeds.SelectMany(s => s.Items).Select(l => l.ItemId).ToHashSet();
        var customers = await context
            .Customers.Where(c => customerIds.Contains(c.ExternalId))
            .ToDictionaryAsync(c => c.ExternalId);
        var items = await context
            .Items.Where(i => itemIds.Contains(i.ExternalBatchId))
            .ToDictionaryAsync(i => i.ExternalBatchId);

        if (customers.Count < customerIds.Count || items.Count < itemIds.Count)
        {
            Log.Warning(
                "--> Skipping order seed data: the seeded customers and items have not been synced. "
                    + "Start the Customer and Inventory services, then restart the Order service."
            );
            return;
        }

        Log.Information("--> Seeding orders...");
        foreach (var seed in seeds)
        {
            var order = new Order
            {
                Id = seed.Id,
                OrderDate = seed.OrderDate,
                ShippedDate = seed.ShippedDate,
                ShippingAddress = seed.ShippingAddress,
                OrderStatus = seed.OrderStatus,
                CustomerId = seed.CustomerId,
                Customer = customers[seed.CustomerId],
            };
            order.OrderItems = seed
                .Items.Select(line => new OrderItem
                {
                    Order = order,
                    OrderId = order.Id,
                    Item = items[line.ItemId],
                    ItemId = line.ItemId,
                    OrderItemQuantity = line.Quantity,
                })
                .ToList();
            if (seed.Invoice != null)
            {
                order.Invoice = new Invoice
                {
                    Order = order,
                    OrderId = order.Id,
                    InvoiceDate = seed.Invoice.InvoiceDate,
                    InvoiceStatus = seed.Invoice.InvoiceStatus,
                    TotalAmount = order.OrderItems.Sum(l =>
                        (l.Item.Price ?? 0m) * l.OrderItemQuantity
                    ),
                };
            }
            context.Orders.Add(order);
        }
        await context.SaveChangesAsync();
        Log.Information(
            "--> Seeded {Count} orders and {Invoices} invoices.",
            seeds.Count,
            seeds.Count(s => s.Invoice != null)
        );
    }

    private static async Task RunMigrations(OrderContext context)
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

    private sealed record OrderSeed(
        Guid Id,
        DateTime? OrderDate,
        DateTime? ShippedDate,
        string? ShippingAddress,
        string? OrderStatus,
        Guid CustomerId,
        List<OrderLineSeed> Items,
        InvoiceSeed? Invoice
    );

    private sealed record OrderLineSeed(Guid ItemId, int Quantity);

    private sealed record InvoiceSeed(DateTime? InvoiceDate, string? InvoiceStatus);
}
