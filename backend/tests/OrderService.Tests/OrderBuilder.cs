using Bogus.Extensions;
using OrderService.Dtos;
using OrderService.Models;
using OrderService.SyncDataServices.Grpc;
using Sava.Tests.Shared;

namespace OrderService.Tests;

// Fake data from Bogus; lengths are clamped to the model's [MaxLength] limits.
internal static class OrderBuilder
{
    private static readonly string[] Units = ["Piece", "Kilogram", "Liter", "Pack"];
    private static readonly decimal[] VatRates = [0m, 9m, 20m];

    public static CustomerSnapshot Customer()
    {
        var f = TestData.Faker;
        return new()
        {
            Name = f.Name.FullName().ClampLength(max: 100),
            CompanyName = f.Company.CompanyName().ClampLength(max: 200),
            Email = f.Internet.Email().ClampLength(max: 100),
            PhoneNumber = f.Random.Replace("+359#########"),
            Address = f.Address.StreetAddress().ClampLength(max: 500),
            City = f.Address.City().ClampLength(max: 100),
            Country = f.Address.Country().ClampLength(max: 100),
            BankName = f.Company.CompanyName().ClampLength(max: 100),
            IBAN = f.Finance.Iban().ClampLength(max: 35),
            BIC = f.Finance.Bic().ClampLength(max: 15),
            VATNumber = f.Random.Replace("BG#########"),
            UIC = f.Random.Replace("#########"),
        };
    }

    public static ProductSnapshot Product()
    {
        var f = TestData.Faker;
        return new(
            f.Random.Guid(),
            f.Random.Replace("???-#####"),
            f.Commerce.ProductName().ClampLength(max: 100),
            f.PickRandom(Units),
            f.Finance.Amount(1, 100),
            f.PickRandom(VatRates),
            f.Finance.Amount(0.5m, 50)
        );
    }

    public static BatchSnapshot Batch(Guid productId)
    {
        var f = TestData.Faker;
        return new(
            f.Random.Guid(),
            productId,
            f.Random.Replace("LOT-#####"),
            TestData.Date(2027, 2035)
        );
    }

    public static CatalogSnapshot Catalog(
        IEnumerable<ProductSnapshot> products,
        IEnumerable<BatchSnapshot> batches
    ) => new(products.ToDictionary(p => p.Id), batches.ToDictionary(b => b.Id));

    public static CatalogSnapshot EmptyCatalog() => Catalog([], []);

    // What InventoryService answers for these ids: only the ones it knows.
    public static CatalogSnapshot Filter(
        this CatalogSnapshot catalog,
        IEnumerable<Guid> productIds,
        IEnumerable<Guid> batchIds
    ) =>
        Catalog(
            productIds.Where(catalog.Products.ContainsKey).Select(id => catalog.Products[id]),
            batchIds.Where(catalog.Batches.ContainsKey).Select(id => catalog.Batches[id])
        );

    // Answers every GetOrderSnapshotAsync call from the catalog, like InventoryService would.
    // The arguments are null while a test reconfigures the call (NSubstitute invokes it then).
    public static void Serves(this IInventoryDataClient client, CatalogSnapshot catalog) =>
        client
            .GetOrderSnapshotAsync(default!, default!)
            .ReturnsForAnyArgs(ci =>
                catalog.Filter(
                    ci.ArgAt<IReadOnlyCollection<Guid>?>(0) ?? [],
                    ci.ArgAt<IReadOnlyCollection<Guid>?>(1) ?? []
                )
            );

    // A item as OrderItems.Apply snapshots it: UnitPrice is the list price.
    public static OrderItem Item(
        ProductSnapshot product,
        params (BatchSnapshot Batch, int Quantity)[] batches
    ) =>
        new()
        {
            ProductId = product.Id,
            SKU = product.SKU,
            Name = product.Name,
            UnitOfMeasure = product.UnitOfMeasure,
            ListPrice = product.ListPrice,
            UnitPrice = product.ListPrice,
            VatRate = product.VatRate,
            Cost = product.Cost,
            Batches =
            [
                .. batches.Select(b => new OrderItemBatch
                {
                    BatchId = b.Batch.Id,
                    Lot = b.Batch.Lot,
                    ExpirationDate = b.Batch.ExpirationDate,
                    Quantity = b.Quantity,
                }),
            ],
        };

    // A item for a new product with batchCount new batches of it.
    public static OrderItem Item(int batchCount = 1)
    {
        var product = Product();
        return Item(
            product,
            [
                .. Enumerable
                    .Range(0, batchCount)
                    .Select(_ => (Batch(product.Id), TestData.Faker.Random.Int(1, 20))),
            ]
        );
    }

    public static Order Order(params OrderItem[] items) =>
        OrderFor(TestData.Faker.Random.Guid(), items);

    public static Order OrderFor(Guid customerId, params OrderItem[] items)
    {
        var f = TestData.Faker;
        return new()
        {
            Id = f.Random.Guid(),
            OrderDate = TestData.Date(2024, 2026),
            ShippingAddress = ShippingAddress(),
            CustomerId = customerId,
            Customer = Customer(),
            Items = [.. items],
        };
    }

    // The batches of a item as they would be requested again.
    public static OrderItemWriteDto ToWriteDto(this OrderItem item) =>
        new(
            item.ProductId,
            null,
            [.. item.Batches.Select(b => new OrderItemBatchWriteDto(b.BatchId, b.Quantity))]
        );

    public static OrderItemWriteDto ItemDto(
        Guid productId,
        params (Guid BatchId, int Quantity)[] batches
    ) =>
        new(
            productId,
            null,
            [.. batches.Select(b => new OrderItemBatchWriteDto(b.BatchId, b.Quantity))]
        );

    public static OrderCreateDto CreateDto(Guid customerId, params OrderItemWriteDto[] items) =>
        new(
            TestData.Date(2024, 2026),
            TestData.Faker.Address.FullAddress().ClampLength(max: 500),
            customerId,
            [.. items]
        );

    public static OrderUpdateDto UpdateDto(
        Guid? customerId = null,
        params OrderItemWriteDto[] items
    ) => new(null, null, customerId, items.Length == 0 ? null : [.. items]);

    // An update that changes only the shipping address.
    public static OrderUpdateDto AddressUpdateDto() => new(null, ShippingAddress(), null, null);

    public static string ShippingAddress() =>
        TestData.Faker.Address.FullAddress().ClampLength(max: 500);

    public static OrderStatusChangeDto StatusDto(OrderStatus status) => new(status);
}
