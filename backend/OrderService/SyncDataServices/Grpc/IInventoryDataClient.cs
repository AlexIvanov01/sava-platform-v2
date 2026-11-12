using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OrderService.SyncDataServices.Grpc;

public interface IInventoryDataClient
{
    // Throws UpstreamUnavailableException when InventoryService doesn't answer in time.
    Task<CatalogSnapshot> GetOrderSnapshotAsync(
        IReadOnlyCollection<Guid> productIds,
        IReadOnlyCollection<Guid> batchIds
    );
}
