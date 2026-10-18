using OrderService.Dtos;
using OrderService.Mappings;
using OrderService.Models;

namespace OrderService.Tests;

public class OrderMappingsTests
{
    [Fact]
    public void ToFullReadDto_FlattensItemsCustomerAndInvoice()
    {
        var customer = OrderBuilder.Customer();
        var item = OrderBuilder.Item("Widget", 2.5m);
        var order = OrderBuilder.Order(customer, (item, 4));
        order.Customer = customer;
        order.Invoice = new Invoice
        {
            Id = 7,
            OrderId = order.Id,
            InvoiceStatus = "Paid",
            TotalAmount = 10m,
        };

        var dto = order.ToFullReadDto();

        dto.Id.ShouldBe(order.Id);
        dto.OrderStatus.ShouldBe("New");
        dto.Customer.ShouldNotBeNull().ExternalId.ShouldBe(customer.ExternalId);
        dto.Invoice.ShouldNotBeNull().Id.ShouldBe(7);
        var line = dto.Items.ShouldHaveSingleItem();
        line.ExternalBatchId.ShouldBe(item.ExternalBatchId);
        line.ExternalProductId.ShouldBe(item.ExternalProductId);
        line.Name.ShouldBe("Widget");
        line.Price.ShouldBe(2.5m);
        line.Lot.ShouldBe(item.Lot);
        line.ExpirationDate.ShouldBe(item.ExpirationDate);
        line.Quantity.ShouldBe(4);
    }

    [Fact]
    public void ToFullReadDto_FromInvoice_UsesThatInvoice()
    {
        var order = OrderBuilder.Order(OrderBuilder.Customer());
        var invoice = new Invoice
        {
            Id = 3,
            Order = order,
            OrderId = order.Id,
        };

        var dto = invoice.ToFullReadDto();

        dto.Id.ShouldBe(order.Id);
        dto.Invoice.ShouldNotBeNull().Id.ShouldBe(3);
        dto.Customer.ShouldBeNull();
    }

    [Fact]
    public void ToReadDto_MapsOrderAndDefaultsNulls()
    {
        var order = OrderBuilder.Order(OrderBuilder.Customer(), (OrderBuilder.Item(), 2));
        order.CustomerId = null;
        order.OrderDate = null;

        var dto = order.ToReadDto();

        dto.CustomerId.ShouldBe(Guid.Empty);
        dto.OrderDate.ShouldBe(default);
        dto.Invoice.ShouldBeNull();
        dto.OrderItems.ShouldNotBeNull().ShouldHaveSingleItem().OrderItemQuantity.ShouldBe(2);
    }

    [Fact]
    public void ToReadDto_Invoice_DefaultsNulls()
    {
        var dto = new Invoice { Id = 1 }.ToReadDto();

        dto.InvoiceDate.ShouldBe(default);
        dto.TotalAmount.ShouldBe(0m);
    }

    [Fact]
    public void ToEntity_FromCreateDto_MapsLines()
    {
        var customerId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var order = OrderBuilder.CreateDto(customerId, (itemId, 3)).ToEntity();

        order.CustomerId.ShouldBe(customerId);
        order.ShippingAddress.ShouldBe("1 Vitosha Blvd");
        var line = order.OrderItems.ShouldHaveSingleItem();
        line.ItemId.ShouldBe(itemId);
        line.OrderItemQuantity.ShouldBe(3);
    }

    [Fact]
    public void ToEntity_FromUpdateDto_NullItemsBecomeEmpty()
    {
        var id = Guid.NewGuid();

        var order = OrderBuilder.UpdateDto(status: "Shipped").ToEntity(id);

        order.Id.ShouldBe(id);
        order.OrderStatus.ShouldBe("Shipped");
        order.ShippingAddress.ShouldBeNull();
        order.OrderItems.ShouldBeEmpty();
    }

    [Fact]
    public void ToEntity_InvoiceDtos()
    {
        var created = new InvoiceCreateDto(
            null,
            new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            "Draft",
            5m
        ).ToEntity();
        var updated = new InvoiceUpdateDto(null, "Paid", null).ToEntity(9);

        created.Id.ShouldBe(0);
        created.InvoiceStatus.ShouldBe("Draft");
        created.TotalAmount.ShouldBe(5m);
        updated.Id.ShouldBe(9);
        updated.InvoiceStatus.ShouldBe("Paid");
        updated.TotalAmount.ShouldBeNull();
    }

    [Fact]
    public void ToPublishedDtos_MapsItemIdsAndQuantities()
    {
        var itemId = Guid.NewGuid();
        OrderItem[] items = [new() { ItemId = itemId, OrderItemQuantity = 2 }];
        OrderItemUpdateDto[] updates = [new(itemId, 5)];

        items.ToPublishedDtos().ShouldBe([new OrderItemPublishedDto(itemId, 2)]);
        updates.ToPublishedDtos().ShouldBe([new OrderItemPublishedDto(itemId, 5)]);
    }
}
