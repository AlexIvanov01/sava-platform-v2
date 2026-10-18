using System.Collections.Generic;
using OrderService.Models;

namespace OrderService.SyncDataServices.Grpc;

public interface IInventoryDataClient
{
    IEnumerable<Item>? ReturnAllItems();
}
