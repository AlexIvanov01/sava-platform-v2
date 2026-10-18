using Bogus.Extensions;
using OrderService.Dtos;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

// Fake data from Bogus; lengths are clamped to the model's [MaxLength] limits.
internal static class OrderBuilder
{
    private static readonly string[] Statuses = ["New", "Confirmed", "Shipped"];

    public static Customer Customer(string? name = null)
    {
        var f = TestData.Faker;
        return new()
        {
            ExternalId = f.Random.Guid(),
            Name = name ?? f.Name.FullName(),
            CompanyName = f.Company.CompanyName(),
            Email = f.Internet.Email().ClampLength(max: 100),
            City = f.Address.City(),
            IBAN = f.Finance.Iban(),
            VATNumber = f.Random.Replace("BG#########"),
        };
    }

    public static Item Item(string? name = null, decimal? price = null)
    {
        var f = TestData.Faker;
        return new()
        {
            ExternalBatchId = f.Random.Guid(),
            ExternalProductId = f.Random.Guid(),
            Name = name ?? f.Commerce.ProductName(),
            Price = price ?? f.Finance.Amount(1, 100),
            Lot = f.Random.Replace("LOT-#####"),
            ExpirationDate = TestData.Date(2027, 2035),
        };
    }

    // Sets both navigations explicitly: OrderItem defaults them to new (empty) entities, which EF would insert.
    public static Order Order(Customer customer, params (Item Item, int Quantity)[] lines)
    {
        var f = TestData.Faker;
        var order = new Order
        {
            Id = f.Random.Guid(),
            OrderDate = TestData.Date(2024, 2026),
            ShippingAddress = f.Address.FullAddress(),
            OrderStatus = f.PickRandom(Statuses),
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
            TestData.Date(2024, 2026),
            null,
            TestData.Faker.Address.FullAddress(),
            TestData.Faker.PickRandom(Statuses),
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
