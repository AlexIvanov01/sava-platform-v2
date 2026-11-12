using System;
using System.Threading.Tasks;
using OrderService.Models;

namespace OrderService.SyncDataServices.Grpc;

public interface ICustomerDataClient
{
    // Null when the customer doesn't exist. Throws UpstreamUnavailableException when
    // CustomerService doesn't answer in time.
    Task<CustomerSnapshot?> GetCustomerAsync(Guid id);
}
