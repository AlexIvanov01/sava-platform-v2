using OrderService.Dtos;
using OrderService.Models;

namespace OrderService.Tests;

internal static class OrderBuilder
{
    public static Customer Customer(string name = "Ivan Petrov") =>
        new()
        {
            ExternalId = Guid.NewGuid(),
            Name = name,
            CompanyName = "Petrov EOOD",
            Email = "ivan@example.com",
            City = "Sofia",
            IBAN = "BG80BNBG96611020345678",
            VATNumber = "BG123456789",
        };

    public static Item Item(string name = "Widget", decimal price = 12.99m) =>
        new()
        {
            ExternalBatchId = Guid.NewGuid(),
            ExternalProductId = Guid.NewGuid(),
            Name = name,
            Price = price,
            Lot = "LOT-1",
            ExpirationDate = new DateTime(2030, 1, 15, 0, 0, 0, DateTimeKind.Unspecified),
        };

    // Sets both navigations explicitly: OrderItem defaults them to new (empty) entities, which EF would insert.
    public static Order Order(Customer customer, params (Item Item, int Quantity)[] lines)
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrderDate = new DateTime(2025, 3, 1, 10, 0, 0, DateTimeKind.Unspecified),
            ShippingAddress = "1 Vitosha Blvd",
            OrderStatus = "New",
            CustomerId = customer.ExternalId,
        };
        order.OrderItems = lines
            .Select(l => new OrderItem
            {
                Order = order,
                OrderId = order.Id,
                Item = l.Item,
                ItemId = l.Item.ExternalBatchId,
                OrderItemQuantity = l.Quantity,
            })
            .ToList();
        return order;
    }

    public static OrderCreateDto CreateDto(
        Guid customerId,
        params (Guid ItemId, int Quantity)[] lines
    ) =>
        new(
            new DateTime(2025, 3, 1, 10, 0, 0, DateTimeKind.Unspecified),
            null,
            "1 Vitosha Blvd",
            "New",
            customerId,
            [.. lines.Select(l => new OrderItemCreateDto(l.ItemId, l.Quantity))]
        );

    public static OrderUpdateDto UpdateDto(
        string? status = null,
        Guid? customerId = null,
        params (Guid ItemId, int Quantity)[] lines
    ) =>
        new(
            null,
            null,
            null,
            status,
            customerId,
            lines.Length == 0
                ? null
                : [.. lines.Select(l => new OrderItemUpdateDto(l.ItemId, l.Quantity))]
        );
}

internal static class OrderDtoTestExtensions
{
    public static Order ToEntityForTest(this OrderCreateDto dto) =>
        OrderService.Mappings.OrderMappings.ToEntity(dto);

    public static Order ToEntityForTest(this OrderUpdateDto dto, Guid id) =>
        OrderService.Mappings.OrderMappings.ToEntity(dto, id);
}
