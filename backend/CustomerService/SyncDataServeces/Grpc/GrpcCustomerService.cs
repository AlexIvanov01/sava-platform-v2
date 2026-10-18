using System.Threading.Tasks;
using CustomerService.DataAccess;
using CustomerService.Mappings;
using Grpc.Core;

namespace CustomerService.SyncDataServeces.Grpc;

public class GrpcCustomerService : GrpcCustomer.GrpcCustomerBase
{
    private readonly ICustomerRepo _repository;

    public GrpcCustomerService(ICustomerRepo repository)
    {
        _repository = repository;
    }

    public override async Task<CustomerResponse> GetAllCustomers(
        GetAllRequest request,
        ServerCallContext context
    )
    {
        var response = new CustomerResponse();

        var customers = await _repository.GetAllCustomersAsync();

        foreach (var customer in customers)
        {
            response.Customers.Add(customer.ToGrpcModel());
        }
        return response;
    }
}
