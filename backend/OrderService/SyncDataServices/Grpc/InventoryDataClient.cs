using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using InventoryService;
using Microsoft.Extensions.Configuration;
using OrderService.Mappings;
using Serilog;

namespace OrderService.SyncDataServices.Grpc;

// Singleton: one channel, reused for every call.
public sealed class InventoryDataClient : IInventoryDataClient, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly GrpcInventory.GrpcInventoryClient _client;
    private readonly TimeSpan _timeout;

    public InventoryDataClient(IConfiguration configuration)
    {
        _channel = GrpcChannel.ForAddress(
            configuration["GrpcInventoryService"]
                ?? throw new InvalidOperationException("GrpcInventoryService is not configured.")
        );
        _client = new GrpcInventory.GrpcInventoryClient(_channel);
        _timeout = GrpcTimeout.From(configuration);
    }

    public async Task<CatalogSnapshot> GetOrderSnapshotAsync(
        IReadOnlyCollection<Guid> productIds,
        IReadOnlyCollection<Guid> batchIds
    )
    {
        var request = new OrderSnapshotRequest();
        request.ProductIds.AddRange(productIds.Select(id => id.ToString()));
        request.BatchIds.AddRange(batchIds.Select(id => id.ToString()));

        try
        {
            var reply = await _client.GetOrderSnapshotAsync(
                request,
                deadline: DateTime.UtcNow.Add(_timeout)
            );
            return new CatalogSnapshot(
                reply.Products.Select(p => p.ToSnapshot()).ToDictionary(p => p.Id),
                reply.Batches.Select(b => b.ToSnapshot()).ToDictionary(b => b.Id)
            );
        }
        catch (RpcException ex)
        {
            Log.Error(ex, "--> InventoryService GetOrderSnapshot failed: {Status}", ex.StatusCode);
            throw new UpstreamUnavailableException("InventoryService", ex);
        }
    }

    public void Dispose() => _channel.Dispose();
}
