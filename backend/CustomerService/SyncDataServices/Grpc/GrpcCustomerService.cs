using System;
using System.Threading.Tasks;
using CustomerService.DataAccess;
using CustomerService.Mappings;
using Grpc.Core;

namespace CustomerService.SyncDataServices.Grpc;

public class GrpcCustomerService : GrpcCustomer.GrpcCustomerBase
{
    private readonly ICustomerRepo _repository;

    public GrpcCustomerService(ICustomerRepo repository)
    {
        _repository = repository;
    }

    public override async Task<GrpcCustomerModel> GetCustomer(
        GetCustomerRequest request,
        ServerCallContext context
    )
    {
        if (!Guid.TryParse(request.Id, out var id))
        {
            throw new RpcException(
                new Status(StatusCode.InvalidArgument, $"Invalid customer id '{request.Id}'.")
            );
        }

        var customer =
            await _repository.GetCustomerAsync(id)
            ?? throw new RpcException(new Status(StatusCode.NotFound, $"Customer {id} not found."));

        return customer.ToGrpcModel();
    }
}
