using System.Threading.Tasks;
using CustomerService.Dtos;

namespace CustomerService.AsyncDataServices;

public interface IMessageBusClient
{
    Task PublishNewCustomerAsync(CustomerPublishedDto customerPublishedDto);
}
