using System.Threading.Tasks;
using OrderService.Dtos;

namespace OrderService.AsyncDataServices;

public interface IMessageBusClient
{
    Task PublishNewOrderAsync(OrderPublishedEvent order);
}
