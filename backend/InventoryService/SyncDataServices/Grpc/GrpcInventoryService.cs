using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using InventoryService.DataAccess;
using InventoryService.Mappings;

namespace InventoryService.SyncDataServices.Grpc;

public class GrpcInventoryService : GrpcInventory.GrpcInventoryBase
{
    private readonly IProductRepo _repository;
    private readonly TimeProvider _timeProvider;

    public GrpcInventoryService(IProductRepo repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public override async Task<OrderSnapshotResponse> GetOrderSnapshot(
        OrderSnapshotRequest request,
        ServerCallContext context
    )
    {
        var productIds = ParseIds(request.ProductIds, "product");
        var batchIds = ParseIds(request.BatchIds, "batch");
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);

        var products = await _repository.GetProductsByIdsAsync(productIds);
        var batches = await _repository.GetBatchesByIdsAsync(batchIds);

        var response = new OrderSnapshotResponse();
        response.Products.AddRange(products.Select(p => p.ToSnapshot(today)));
        response.Batches.AddRange(batches.Select(b => b.ToSnapshot()));
        response.MissingProductIds.AddRange(
            productIds.Except(products.Select(p => p.Id)).Select(id => id.ToString())
        );
        response.MissingBatchIds.AddRange(
            batchIds.Except(batches.Select(b => b.Id)).Select(id => id.ToString())
        );
        return response;
    }

    private static List<Guid> ParseIds(IEnumerable<string> ids, string kind) =>
        ids.Select(id =>
                Guid.TryParse(id, out var guid)
                    ? guid
                    : throw new RpcException(
                        new Status(StatusCode.InvalidArgument, $"Invalid {kind} id '{id}'.")
                    )
            )
            .Distinct()
            .ToList();
}
