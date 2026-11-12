using OrderService.Models;
using OrderService.Ordering;
using OrderService.SyncDataServices.Grpc;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class OrderItemsTests
{
    // A product with two batches and the catalog that knows them.
    private static (ProductSnapshot Product, BatchSnapshot[] Batches) ProductWithBatches()
    {
        var product = OrderBuilder.Product();
        return (product, [OrderBuilder.Batch(product.Id), OrderBuilder.Batch(product.Id)]);
    }

    private static int Quantity() => TestData.Faker.Random.Int(1, 50);

    // What an order item holds, to check that a failed Apply changed nothing.
    private static List<string> Describe(Order order) =>
        [
            .. order.Items.Select(l =>
                $"{l.ProductId}|{l.SKU}|{l.Name}|{l.UnitPrice}|{l.ListPrice}|"
                + string.Join(",", l.Batches.Select(b => $"{b.BatchId}:{b.Lot}:{b.Quantity}"))
            ),
        ];

    [Fact]
    public void SnapshotIds_NewOrder_AsksForEveryProductAndBatch()
    {
        var (a, aBatches) = ProductWithBatches();
        var (b, bBatches) = ProductWithBatches();

        var (productIds, batchIds) = OrderItems.SnapshotIds(
            [],
            [
                OrderBuilder.ItemDto(a.Id, (aBatches[0].Id, 1), (aBatches[1].Id, 2)),
                OrderBuilder.ItemDto(b.Id, (bBatches[0].Id, 3)),
            ]
        );

        productIds.ShouldBe([a.Id, b.Id]);
        batchIds.ShouldBe([aBatches[0].Id, aBatches[1].Id, bBatches[0].Id]);
    }

    [Fact]
    public void SnapshotIds_ExistingItem_AsksOnlyForWhatIsNew()
    {
        var (a, aBatches) = ProductWithBatches();
        var (b, bBatches) = ProductWithBatches();
        var existing = OrderBuilder.Item(a, (aBatches[0], Quantity()));

        var (productIds, batchIds) = OrderItems.SnapshotIds(
            [existing],
            [
                OrderBuilder.ItemDto(a.Id, (aBatches[0].Id, Quantity()), (aBatches[1].Id, 1)),
                OrderBuilder.ItemDto(b.Id, (bBatches[0].Id, 1)),
            ]
        );

        productIds.ShouldBe([b.Id]);
        batchIds.ShouldBe([aBatches[1].Id, bBatches[0].Id]);
    }

    [Fact]
    public void SnapshotIds_NothingNew_IsEmpty()
    {
        var item = OrderBuilder.Item(batchCount: 2);

        var (productIds, batchIds) = OrderItems.SnapshotIds([item], [item.ToWriteDto()]);

        productIds.ShouldBeEmpty();
        batchIds.ShouldBeEmpty();
    }

    [Fact]
    public void Apply_NewLine_SnapshotsProductAndBatches()
    {
        var (product, batches) = ProductWithBatches();
        var order = OrderBuilder.Order();
        var (q0, q1) = (Quantity(), Quantity());

        var errors = OrderItems.Apply(
            order,
            [OrderBuilder.ItemDto(product.Id, (batches[0].Id, q0), (batches[1].Id, q1))],
            OrderBuilder.Catalog([product], batches)
        );

        errors.ShouldBeEmpty();
        var item = order.Items.ShouldHaveSingleItem();
        item.ProductId.ShouldBe(product.Id);
        item.SKU.ShouldBe(product.SKU);
        item.Name.ShouldBe(product.Name);
        item.UnitOfMeasure.ShouldBe(product.UnitOfMeasure);
        item.ListPrice.ShouldBe(product.ListPrice);
        item.UnitPrice.ShouldBe(product.ListPrice);
        item.VatRate.ShouldBe(product.VatRate);
        item.Cost.ShouldBe(product.Cost);
        item.Quantity.ShouldBe(q0 + q1);
        item.Batches.Select(b => (b.BatchId, b.Lot, b.ExpirationDate, b.Quantity))
            .ShouldBe(
                [
                    (batches[0].Id, batches[0].Lot, batches[0].ExpirationDate, q0),
                    (batches[1].Id, batches[1].Lot, batches[1].ExpirationDate, q1),
                ],
                ignoreOrder: true
            );
    }

    [Fact]
    public void Apply_NewLineWithUnitPrice_OverridesListPrice()
    {
        var (product, batches) = ProductWithBatches();
        var order = OrderBuilder.Order();
        var price = TestData.Faker.Finance.Amount(1, 100);

        OrderItems
            .Apply(
                order,
                [OrderBuilder.ItemDto(product.Id, (batches[0].Id, 1)) with { UnitPrice = price }],
                OrderBuilder.Catalog([product], batches)
            )
            .ShouldBeEmpty();

        var item = order.Items.ShouldHaveSingleItem();
        item.UnitPrice.ShouldBe(price);
        item.ListPrice.ShouldBe(product.ListPrice);
    }

    [Fact]
    public void Apply_NewProductWithoutListPrice_NeedsUnitPrice()
    {
        var (product, batches) = ProductWithBatches();
        product = product with { ListPrice = null };
        var catalog = OrderBuilder.Catalog([product], batches);
        var request = OrderBuilder.ItemDto(product.Id, (batches[0].Id, 1));
        var order = OrderBuilder.Order();

        OrderItems
            .Apply(order, [request], catalog)
            .ShouldHaveSingleItem()
            .ShouldBe($"Product {product.Id} has no list price, so a UnitPrice is required.");
        order.Items.ShouldBeEmpty();

        var price = TestData.Faker.Finance.Amount(1, 100);
        OrderItems.Apply(order, [request with { UnitPrice = price }], catalog).ShouldBeEmpty();
        var item = order.Items.ShouldHaveSingleItem();
        item.ListPrice.ShouldBeNull();
        item.UnitPrice.ShouldBe(price);
    }

    [Fact]
    public void Apply_ExistingItem_KeepsItsSnapshotEvenIfTheCatalogChanged()
    {
        var (product, batches) = ProductWithBatches();
        var item = OrderBuilder.Item(product, (batches[0], Quantity()));
        var order = OrderBuilder.Order(item);
        var changedProduct = OrderBuilder.Product() with { Id = product.Id };
        var changedBatch = OrderBuilder.Batch(product.Id) with { Id = batches[0].Id };
        var quantity = Quantity();

        OrderItems
            .Apply(
                order,
                [OrderBuilder.ItemDto(product.Id, (batches[0].Id, quantity))],
                OrderBuilder.Catalog([changedProduct], [changedBatch])
            )
            .ShouldBeEmpty();

        var kept = order.Items.ShouldHaveSingleItem();
        kept.ShouldBeSameAs(item);
        kept.SKU.ShouldBe(product.SKU);
        kept.Name.ShouldBe(product.Name);
        kept.UnitOfMeasure.ShouldBe(product.UnitOfMeasure);
        kept.ListPrice.ShouldBe(product.ListPrice);
        kept.UnitPrice.ShouldBe(product.ListPrice);
        kept.VatRate.ShouldBe(product.VatRate);
        kept.Cost.ShouldBe(product.Cost);
        var batch = kept.Batches.ShouldHaveSingleItem();
        batch.Lot.ShouldBe(batches[0].Lot);
        batch.ExpirationDate.ShouldBe(batches[0].ExpirationDate);
        batch.Quantity.ShouldBe(quantity);
    }

    // The existing item's product and batches aren't in the catalog at all: they aren't needed.
    [Fact]
    public void Apply_ExistingItem_UnitPriceIsKeptOrUpdated()
    {
        var item = OrderBuilder.Item();
        item.UnitPrice = TestData.Faker.Finance.Amount(1, 100);
        var original = item.UnitPrice;
        var order = OrderBuilder.Order(item);

        OrderItems.Apply(order, [item.ToWriteDto()], OrderBuilder.EmptyCatalog()).ShouldBeEmpty();
        item.UnitPrice.ShouldBe(original);

        var price = TestData.Faker.Finance.Amount(101, 200);
        OrderItems
            .Apply(
                order,
                [item.ToWriteDto() with { UnitPrice = price }],
                OrderBuilder.EmptyCatalog()
            )
            .ShouldBeEmpty();
        item.UnitPrice.ShouldBe(price);
        item.ListPrice.ShouldNotBe(price);
    }

    [Fact]
    public void Apply_ExistingItem_UpdatesAddsAndRemovesBatches()
    {
        var product = OrderBuilder.Product();
        var kept = OrderBuilder.Batch(product.Id);
        var removed = OrderBuilder.Batch(product.Id);
        var added = OrderBuilder.Batch(product.Id);
        var item = OrderBuilder.Item(product, (kept, Quantity()), (removed, Quantity()));
        var order = OrderBuilder.Order(item);
        var (keptQuantity, addedQuantity) = (Quantity(), Quantity());

        OrderItems
            .Apply(
                order,
                [
                    OrderBuilder.ItemDto(
                        product.Id,
                        (kept.Id, keptQuantity),
                        (added.Id, addedQuantity)
                    ),
                ],
                OrderBuilder.Catalog([], [added])
            )
            .ShouldBeEmpty();

        order.Items.ShouldHaveSingleItem().ShouldBeSameAs(item);
        item.Batches.Select(b => (b.BatchId, b.Lot, b.ExpirationDate, b.Quantity))
            .ShouldBe(
                [
                    (kept.Id, kept.Lot, kept.ExpirationDate, keptQuantity),
                    (added.Id, added.Lot, added.ExpirationDate, addedQuantity),
                ],
                ignoreOrder: true
            );
        item.Quantity.ShouldBe(keptQuantity + addedQuantity);
    }

    [Fact]
    public void Apply_RemovesItemsThatAreNotRequested()
    {
        var keep = OrderBuilder.Item();
        var drop = OrderBuilder.Item(batchCount: 2);
        var (product, batches) = ProductWithBatches();
        var order = OrderBuilder.Order(keep, drop);

        OrderItems
            .Apply(
                order,
                [keep.ToWriteDto(), OrderBuilder.ItemDto(product.Id, (batches[1].Id, 1))],
                OrderBuilder.Catalog([product], batches)
            )
            .ShouldBeEmpty();

        order
            .Items.Select(l => l.ProductId)
            .ShouldBe([keep.ProductId, product.Id], ignoreOrder: true);
        order.Items.ShouldContain(keep);
    }

    [Fact]
    public void Apply_UnknownProduct_IsAnError()
    {
        var (product, batches) = ProductWithBatches();

        var errors = OrderItems.Apply(
            OrderBuilder.Order(),
            [OrderBuilder.ItemDto(product.Id, (batches[0].Id, 1))],
            OrderBuilder.Catalog([], batches)
        );

        errors.ShouldBe([$"Product {product.Id} does not exist."]);
    }

    [Fact]
    public void Apply_UnknownBatch_IsAnError()
    {
        var (product, batches) = ProductWithBatches();

        var errors = OrderItems.Apply(
            OrderBuilder.Order(),
            [OrderBuilder.ItemDto(product.Id, (batches[0].Id, 1), (batches[1].Id, 1))],
            OrderBuilder.Catalog([product], [batches[0]])
        );

        errors.ShouldBe([$"Batch {batches[1].Id} does not exist."]);
    }

    [Fact]
    public void Apply_BatchOfAnotherProduct_IsAnError()
    {
        var (product, batches) = ProductWithBatches();
        var (other, otherBatches) = ProductWithBatches();

        var errors = OrderItems.Apply(
            OrderBuilder.Order(),
            [OrderBuilder.ItemDto(product.Id, (otherBatches[0].Id, 1))],
            OrderBuilder.Catalog([product, other], [.. batches, .. otherBatches])
        );

        errors.ShouldBe([
            $"Batch {otherBatches[0].Id} belongs to another product than {product.Id}.",
        ]);
    }

    // A batch new to an existing item is checked like one on a new item.
    [Fact]
    public void Apply_NewBatchOnExistingItem_IsValidated()
    {
        var item = OrderBuilder.Item();
        var foreign = OrderBuilder.Batch(TestData.Faker.Random.Guid());
        var missing = TestData.Faker.Random.Guid();
        var request = item.ToWriteDto();
        request.Batches!.Add(new(foreign.Id, 1));
        request.Batches.Add(new(missing, 1));

        var errors = OrderItems.Apply(
            OrderBuilder.Order(item),
            [request],
            OrderBuilder.Catalog([], [foreign])
        );

        errors.ShouldBe([
            $"Batch {foreign.Id} belongs to another product than {item.ProductId}.",
            $"Batch {missing} does not exist.",
        ]);
    }

    [Fact]
    public void Apply_Errors_ReportsAllAndLeavesTheOrderUntouched()
    {
        var keep = OrderBuilder.Item(batchCount: 2);
        var order = OrderBuilder.Order(keep, OrderBuilder.Item());
        var before = Describe(order);
        var (valid, validBatches) = ProductWithBatches();
        var unknownProduct = TestData.Faker.Random.Guid();
        var unknownBatch = TestData.Faker.Random.Guid();
        var changedKeep = keep.ToWriteDto() with
        {
            UnitPrice = TestData.Faker.Finance.Amount(1, 100),
        };
        changedKeep.Batches![0] = changedKeep.Batches[0] with { Quantity = 99 };

        var errors = OrderItems.Apply(
            order,
            [
                changedKeep,
                OrderBuilder.ItemDto(valid.Id, (validBatches[0].Id, 1), (unknownBatch, 1)),
                OrderBuilder.ItemDto(unknownProduct, (validBatches[1].Id, 1)),
            ],
            OrderBuilder.Catalog([valid], validBatches)
        );

        errors.ShouldBe([
            $"Batch {unknownBatch} does not exist.",
            $"Product {unknownProduct} does not exist.",
        ]);
        Describe(order).ShouldBe(before);
    }
}
