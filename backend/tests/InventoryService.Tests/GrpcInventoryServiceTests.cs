using InventoryService.DataAccess;
using InventoryService.Models;
using InventoryService.SyncDataServices.Grpc;

namespace InventoryService.Tests;

public class GrpcInventoryServiceTests
{
    [Fact]
    public async Task GetAllItems_ReturnsOneItemPerBatchWithProductName()
    {
        var repo = Substitute.For<IProductRepo>();
        var widget = InventoryBuilder.Product(
            "Widget",
            InventoryBuilder.Batch("L1"),
            InventoryBuilder.Batch("L2")
        );
        var gadget = InventoryBuilder.Product("Gadget", InventoryBuilder.Batch("L3"));
        var empty = InventoryBuilder.Product("Empty");
        repo.GetAllProductBatchesAsync().Returns(new List<Product> { widget, gadget, empty });

        var response = await new GrpcInventoryService(repo).GetAllItems(new GetAllRequest(), null!);

        response
            .Item.Select(i => (i.Name, i.Lot))
            .ShouldBe([("Widget", "L1"), ("Widget", "L2"), ("Gadget", "L3")]);
    }
}
