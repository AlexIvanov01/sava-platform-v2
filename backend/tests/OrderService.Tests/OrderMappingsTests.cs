using OrderService.Dtos;
using OrderService.Mappings;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class OrderMappingsTests
{
    [Fact]
    public void ToReadDto_MapsOrderCustomerInvoiceItemsAndTotals()
    {
        var items = new[] { OrderBuilder.Item(batchCount: 2), OrderBuilder.Item() };
        var order = OrderBuilder.Order(items);
        order.CreatedBy = TestData.Faker.Internet.UserName();
        order.CreatedAt = TestData.Date(2024, 2025);
        order.Invoice = new Invoice
        {
            Id = TestData.Faker.Random.Int(1, 1000),
            OrderId = order.Id,
            InvoiceStatus = "Paid",
            TotalAmount = order.Items.Gross(),
        };

        var dto = order.ToReadDto();

        dto.Id.ShouldBe(order.Id);
        dto.OrderDate.ShouldBe(order.OrderDate);
        dto.ShippedDate.ShouldBe(order.ShippedDate);
        dto.ShippingAddress.ShouldBe(order.ShippingAddress);
        dto.OrderStatus.ShouldBe(order.OrderStatus);
        dto.CustomerId.ShouldBe(order.CustomerId);
        dto.Customer.ShouldBe(order.Customer!.ToReadDto());
        dto.Invoice.ShouldNotBeNull().Id.ShouldBe(order.Invoice.Id);
        dto.Invoice.TotalAmount.ShouldBe(order.Invoice.TotalAmount.Value);
        dto.Audit.ShouldBe(new AuditReadDto(order.CreatedAt, null, order.CreatedBy, null));
        dto.Items.Select(l => l.ProductId)
            .ShouldBe(items.Select(l => l.ProductId), ignoreOrder: true);
        var cost = order.Items.TotalCost().ShouldNotBeNull();
        var net = order.Items.Net();
        dto.Totals.ShouldBe(
            new OrderTotalsReadDto(
                net,
                order.Items.Vat(),
                order.Items.Gross(),
                cost,
                net - cost,
                OrderFigures.EbitPercent(net, cost)
            )
        );
        dto.Totals.Net.ShouldBe(dto.Items.Sum(l => l.Net));
        dto.Totals.Gross.ShouldBe(dto.Items.Sum(l => l.Gross));
    }

    [Fact]
    public void ToReadDto_Item_MapsSnapshotFiguresAndBatches()
    {
        var item = OrderBuilder.Item(batchCount: 3);
        item.UnitPrice = TestData.Faker.Finance.Amount(1, 100);

        var dto = item.ToReadDto();

        dto.ProductId.ShouldBe(item.ProductId);
        dto.SKU.ShouldBe(item.SKU);
        dto.Name.ShouldBe(item.Name);
        dto.UnitOfMeasure.ShouldBe(item.UnitOfMeasure);
        dto.Quantity.ShouldBe(item.Batches.Sum(b => b.Quantity));
        dto.ListPrice.ShouldBe(item.ListPrice);
        dto.UnitPrice.ShouldBe(item.UnitPrice);
        dto.VatRate.ShouldBe(item.VatRate);
        dto.Cost.ShouldBe(item.Cost);
        dto.Net.ShouldBe(item.Net());
        dto.Vat.ShouldBe(item.Vat());
        dto.Gross.ShouldBe(item.Gross());
        dto.Ebit.ShouldBe(item.Ebit());
        dto.EbitPercent.ShouldBe(item.EbitPercent());
        // Earliest expiry first.
        dto.Batches.ShouldBe([
            .. item
                .Batches.OrderBy(b => b.ExpirationDate)
                .ThenBy(b => b.BatchId)
                .Select(b => new OrderItemBatchReadDto(
                    b.BatchId,
                    b.Lot,
                    b.ExpirationDate,
                    b.Quantity
                )),
        ]);
    }

    [Fact]
    public void ToReadDto_ItemsAreSortedByName()
    {
        var items = Enumerable.Range(0, 4).Select(_ => OrderBuilder.Item()).ToArray();

        var dto = OrderBuilder.Order(items).ToReadDto();

        dto.Items.Select(l => l.Name).ShouldBe(items.Select(l => l.Name).Order());
    }

    [Fact]
    public void ToReadDto_ItemWithoutCost_LeavesOrderCostAndEbitEmpty()
    {
        var withoutCost = OrderBuilder.Item();
        withoutCost.Cost = null;

        var dto = OrderBuilder.Order(OrderBuilder.Item(), withoutCost).ToReadDto();

        dto.Totals.Cost.ShouldBeNull();
        dto.Totals.Ebit.ShouldBeNull();
        dto.Totals.EbitPercent.ShouldBeNull();
        dto.Totals.Net.ShouldBeGreaterThan(0m);
    }

    [Fact]
    public void ToReadDto_EmptyOrder_HasZeroTotals()
    {
        var order = OrderBuilder.Order();
        order.Customer = null;

        var dto = order.ToReadDto();

        dto.Customer.ShouldBeNull();
        dto.Invoice.ShouldBeNull();
        dto.Items.ShouldBeEmpty();
        dto.Totals.ShouldBe(new OrderTotalsReadDto(0m, 0m, 0m, 0m, 0m, null));
    }

    [Fact]
    public void ToOrderReadDto_FromInvoice_UsesThatInvoice()
    {
        var order = OrderBuilder.Order(OrderBuilder.Item());
        var invoice = new Invoice
        {
            Id = TestData.Faker.Random.Int(1, 1000),
            Order = order,
            OrderId = order.Id,
        };

        var dto = invoice.ToOrderReadDto();

        dto.Id.ShouldBe(order.Id);
        dto.Invoice.ShouldNotBeNull().Id.ShouldBe(invoice.Id);
        dto.Items.ShouldHaveSingleItem().ProductId.ShouldBe(order.Items[0].ProductId);
    }

    [Fact]
    public void ToReadDto_Invoice_DefaultsNulls()
    {
        var dto = new Invoice { Id = 1 }.ToReadDto();

        dto.InvoiceDate.ShouldBe(default);
        dto.TotalAmount.ShouldBe(0m);
    }

    [Fact]
    public void ToEntity_FromCreateDto_MapsFieldsWithoutItems()
    {
        var customerId = TestData.Faker.Random.Guid();
        var dto = OrderBuilder.CreateDto(
            customerId,
            OrderBuilder.ItemDto(TestData.Faker.Random.Guid(), (TestData.Faker.Random.Guid(), 1))
        );

        var order = dto.ToEntity();

        order.CustomerId.ShouldBe(customerId);
        order.OrderDate.ShouldBe(dto.OrderDate);
        order.ShippedDate.ShouldBe(dto.ShippedDate);
        order.ShippingAddress.ShouldBe(dto.ShippingAddress);
        order.OrderStatus.ShouldBe(dto.OrderStatus);
        order.Customer.ShouldBeNull();
        order.Items.ShouldBeEmpty();
    }

    [Fact]
    public void ToEntity_InvoiceDtos()
    {
        var date = TestData.Date(2024, 2026);
        var total = TestData.Faker.Finance.Amount(1, 1000);
        var created = new InvoiceCreateDto(null, date, "Draft", total).ToEntity();
        var updated = new InvoiceUpdateDto(null, "Paid", null).ToEntity(9);

        created.Id.ShouldBe(0);
        created.InvoiceDate.ShouldBe(date);
        created.InvoiceStatus.ShouldBe("Draft");
        created.TotalAmount.ShouldBe(total);
        updated.Id.ShouldBe(9);
        updated.InvoiceStatus.ShouldBe("Paid");
        updated.TotalAmount.ShouldBeNull();
    }

    [Fact]
    public void ToPublishedDtos_MapsProductsAndBatchQuantities()
    {
        var items = new[] { OrderBuilder.Item(batchCount: 2), OrderBuilder.Item() };

        var published = items.ToPublishedDtos();

        published.Count.ShouldBe(2);
        for (var i = 0; i < items.Length; i++)
        {
            published[i].ProductId.ShouldBe(items[i].ProductId);
            published[i]
                .Batches.ShouldBe(
                    items[i]
                        .Batches.Select(b => new OrderItemBatchPublishedDto(b.BatchId, b.Quantity))
                );
        }
    }
}
