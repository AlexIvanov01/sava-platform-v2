using System.Threading.Tasks;
using InventoryService.Dtos;

namespace InventoryService.AsyncDataServices;

public interface IMessageBusClient
{
    Task PublishNewItemAsync(ItemPublishedDto itemPublishedDto);
}
