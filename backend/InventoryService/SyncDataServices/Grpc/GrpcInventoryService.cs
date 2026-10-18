using System.Threading.Tasks;
using Grpc.Core;
using InventoryService.DataAccess;
using InventoryService.Mappings;

namespace InventoryService.SyncDataServices.Grpc;

public class GrpcInventoryService : GrpcInventory.GrpcInventoryBase
{
    private readonly IProductRepo _repository;

    public GrpcInventoryService(IProductRepo repository)
    {
        _repository = repository;
    }

    public override async Task<InventoryResponse> GetAllItems(GetAllRequest request, ServerCallContext context)
    {
        var response = new InventoryResponse();

        var products = await _repository.GetAllProductBatchesAsync();

        foreach (var product in products)
        {
            foreach(var batch in product.Batches)
            {
                batch.Product = product;
                var item = batch.ToGrpcModel();
                response.Item.Add(item);
            }
        }
        return response;
    }

}
