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
using OrderService.Dtos;
using OrderService.Models;
using OrderService.Ordering;
using OrderService.SyncDataServices.Grpc;
using Serilog;

namespace OrderService.DataAccess;

public static class PrepDB
{
    public static async Task PrepPopulation(IApplicationBuilder app)
    {
        using var serviceScope = app.ApplicationServices.CreateScope();
        var services = serviceScope.ServiceProvider;
        var env = services.GetRequiredService<IWebHostEnvironment>();
        var context = services.GetRequiredService<OrderContext>();
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

        Log.Information(
            "--> Waiting 5 seconds for Inventory and Customer Service initialization..."
        );
        await Task.Delay(5000);

        await SeedOrdersAsync(
            context,
            services.GetRequiredService<IInventoryDataClient>(),
            services.GetRequiredService<ICustomerDataClient>(),
            Path.Combine(env.ContentRootPath, "dummy_data.json")
        );
    }

    // Seeds orders that reference the customers, products and batches seeded by the Customer and
    // Inventory services, snapshotting them over gRPC the same way saving an order does. Skipped
    // (with a warning) when either service is down or doesn't have the seeded data. Invoice
    // totals are the orders' gross totals.
    public static async Task SeedOrdersAsync(
        OrderContext context,
        IInventoryDataClient inventory,
        ICustomerDataClient customers,
        string seedFile
    )
    {
        if (await context.Orders.AnyAsync())
        {
            Log.Information("--> Orders already present.");
            return;
        }

        string file = await File.ReadAllTextAsync(seedFile);
        var seeds = JsonSerializer.Deserialize<List<OrderSeed>>(file) ?? [];

        var orders = new List<Order>();
        try
        {
            var items = seeds.SelectMany(s => s.Items).ToList();
            var catalog = await inventory.GetOrderSnapshotAsync(
                items.Select(l => l.ProductId!.Value).Distinct().ToList(),
                items.SelectMany(l => l.Batches!).Select(b => b.BatchId!.Value).Distinct().ToList()
            );
            var snapshots = new Dictionary<Guid, CustomerSnapshot>();
            foreach (var customerId in seeds.Select(s => s.CustomerId).Distinct())
            {
                var customer = await customers.GetCustomerAsync(customerId);
                if (customer == null)
                {
                    SkipSeed($"customer {customerId} does not exist");
                    return;
                }
                snapshots[customerId] = customer;
            }

            foreach (var seed in seeds)
            {
                var order = new Order
                {
                    Id = seed.Id,
                    OrderDate = seed.OrderDate,
                    ShippingAddress = seed.ShippingAddress,
                    Status = seed.Status,
                    CompletedDate = seed.CompletedDate,
                    CancelledDate = seed.CancelledDate,
                    CustomerId = seed.CustomerId,
                    Customer = snapshots[seed.CustomerId],
                };
                var errors = OrderItems.Apply(order, seed.Items, catalog);
                if (errors.Count > 0)
                {
                    SkipSeed(string.Join(" ", errors));
                    return;
                }
                if (seed.Invoice != null)
                {
                    order.Invoice = new Invoice
                    {
                        Order = order,
                        OrderId = order.Id,
                        InvoiceDate = seed.Invoice.InvoiceDate,
                        InvoiceStatus = seed.Invoice.InvoiceStatus,
                        TotalAmount = order.Items.Gross(),
                    };
                }
                orders.Add(order);
            }
        }
        catch (UpstreamUnavailableException ex)
        {
            SkipSeed($"{ex.Service} is unavailable");
            return;
        }

        Log.Information("--> Seeding orders...");
        context.Orders.AddRange(orders);
        await context.SaveChangesAsync();
        Log.Information(
            "--> Seeded {Count} orders and {Invoices} invoices.",
            orders.Count,
            orders.Count(o => o.Invoice != null)
        );
    }

    private static void SkipSeed(string reason) =>
        Log.Warning(
            "--> Skipping order seed data: {Reason}. Start the Customer and Inventory services "
                + "with their seed data, then restart the Order service.",
            reason
        );

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
        string? ShippingAddress,
        OrderStatus Status,
        DateTime? CompletedDate,
        DateTime? CancelledDate,
        Guid CustomerId,
        List<OrderItemWriteDto> Items,
        InvoiceSeed? Invoice
    );

    private sealed record InvoiceSeed(DateTime? InvoiceDate, string? InvoiceStatus);
}
