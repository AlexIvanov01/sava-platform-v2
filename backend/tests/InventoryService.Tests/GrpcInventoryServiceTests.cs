using System.Globalization;
using Grpc.Core;
using InventoryService.DataAccess;
using InventoryService.Models;
using InventoryService.SyncDataServices.Grpc;
using Microsoft.Extensions.Time.Testing;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

public class GrpcInventoryServiceTests
{
    private readonly IProductRepo _repo = Substitute.For<IProductRepo>();
    private readonly DateTime _now = TestData.Date(2027, 2030);
    private readonly GrpcInventoryService _service;

    public GrpcInventoryServiceTests() =>
        _service = new GrpcInventoryService(
            _repo,
            new FakeTimeProvider(new DateTimeOffset(_now, TimeSpan.Zero))
        );

    [Fact]
    public async Task GetAllItems_ReturnsOneItemPerBatchWithProductName()
    {
        var widget = InventoryBuilder.Product(
            "Widget",
            InventoryBuilder.Batch("L1"),
            InventoryBuilder.Batch("L2")
        );
        var gadget = InventoryBuilder.Product("Gadget", InventoryBuilder.Batch("L3"));
        var empty = InventoryBuilder.Product("Empty");
        _repo.GetAllProductBatchesAsync().Returns(new List<Product> { widget, gadget, empty });

        var response = await _service.GetAllItems(new GetAllRequest(), null!);

        response
            .Item.Select(i => (i.Name, i.Lot))
            .ShouldBe([("Widget", "L1"), ("Widget", "L2"), ("Gadget", "L3")]);
    }

    [Fact]
    public async Task GetOrderSnapshot_ReturnsProductsAndBatchesAndListsMissingIds()
    {
        var cheap = InventoryBuilder.Batch(quantity: 5);
        var dear = InventoryBuilder.Batch(quantity: 5);
        cheap.PurchasePrice = 2m;
        dear.PurchasePrice = 3.5m;
        cheap.ExpirationDate = dear.ExpirationDate = _now.AddDays(10);
        var product = InventoryBuilder.Product(null, cheap, dear);
        dear.ProductId = cheap.ProductId = product.Id;
        var missingProduct = TestData.Faker.Random.Guid();
        var missingBatch = TestData.Faker.Random.Guid();
        _repo
            .GetProductsByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(new List<Product> { product });
        _repo
            .GetBatchesByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(new List<ProductBatch> { dear });

        var request = new OrderSnapshotRequest
        {
            ProductIds = { product.Id.ToString(), missingProduct.ToString() },
            BatchIds = { dear.Id.ToString(), missingBatch.ToString() },
        };
        var response = await _service.GetOrderSnapshot(request, null!);

        var p = response.Products.ShouldHaveSingleItem();
        p.Id.ShouldBe(product.Id.ToString());
        p.Sku.ShouldBe(product.SKU);
        p.Name.ShouldBe(product.Name);
        p.UnitOfMeasure.ShouldBe(product.UnitOfMeasure.ToString());
        decimal.Parse(p.ListPrice, CultureInfo.InvariantCulture).ShouldBe(product.ListPrice!.Value);
        decimal.Parse(p.VatRate, CultureInfo.InvariantCulture).ShouldBe(product.VatRate!.Value);
        p.Cost.ShouldBe("3.5");
        var b = response.Batches.ShouldHaveSingleItem();
        b.Id.ShouldBe(dear.Id.ToString());
        b.ProductId.ShouldBe(product.Id.ToString());
        b.Lot.ShouldBe(dear.Lot);
        DateTime
            .Parse(b.ExpirationDate, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            .ShouldBe(dear.ExpirationDate.Value);
        response.MissingProductIds.ShouldBe([missingProduct.ToString()]);
        response.MissingBatchIds.ShouldBe([missingBatch.ToString()]);
    }

    [Fact]
    public async Task GetOrderSnapshot_NoActiveBatch_SendsEmptyCost()
    {
        var product = InventoryBuilder.Product(null, InventoryBuilder.Batch(quantity: 0));
        product.ListPrice = null;
        _repo
            .GetProductsByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>())
            .Returns(new List<Product> { product });
        _repo.GetBatchesByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>()).Returns([]);

        var response = await _service.GetOrderSnapshot(
            new OrderSnapshotRequest { ProductIds = { product.Id.ToString() } },
            null!
        );

        response.Products.Single().Cost.ShouldBeEmpty();
        response.Products.Single().ListPrice.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetOrderSnapshot_InvalidId_IsInvalidArgument()
    {
        var ex = await Should.ThrowAsync<RpcException>(() =>
            _service.GetOrderSnapshot(new OrderSnapshotRequest { BatchIds = { "nope" } }, null!)
        );

        ex.StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }
}
