using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderService.DataAccess;
using Serilog;

namespace OrderService.SyncDataServices.Grpc;

public class GrpcSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public GrpcSyncService(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan syncInterval;

        while (!stoppingToken.IsCancellationRequested)
        {
            syncInterval = TimeSpan.FromMinutes(
                double.Parse(_configuration["GrpcSyncIntervalInMinutes"] ?? "5")
            );

            Log.Information("Grpc Sync Interval In Minutes: {Minutes}", syncInterval);

            await Task.Delay(syncInterval, stoppingToken);
            try
            {
                using var serviceScope = _serviceProvider.CreateScope();
                var grpcInventoryClient =
                    serviceScope.ServiceProvider.GetRequiredService<IInventoryDataClient>();
                var grpcCustomerClient =
                    serviceScope.ServiceProvider.GetRequiredService<ICustomerDataClient>();

                var items = grpcInventoryClient.ReturnAllItems();
                var customers = grpcCustomerClient.ReturnAllCustomers();

                if (items != null)
                {
                    await PrepDB.SyncItems(
                        serviceScope.ServiceProvider.GetRequiredService<IItemRepo>(),
                        items
                    );
                }
                if (customers != null)
                {
                    await PrepDB.SyncCustomers(
                        serviceScope.ServiceProvider.GetRequiredService<ICustomerRepo>(),
                        customers
                    );
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "An error occured during data synchronisation: {Ex}", ex.Message);
            }
        }
    }
}
