using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderService.Models;
using OrderService.SyncDataServices.Grpc;
using Serilog;

namespace OrderService.DataAccess;

public static class PrepDB
{
    public static async Task PrepPopulation(IApplicationBuilder app)
    {
        using var serviceScope = app.ApplicationServices.CreateScope();
        await RunMigrations(serviceScope.ServiceProvider.GetRequiredService<OrderContext>());

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
}
