using System;
using System.Collections.Generic;
using System.Linq;
using OrderService.Dtos;
using OrderService.Models;

namespace OrderService.Mappings;

public static class OrderMappings
{
    public static OrderReadDto ToReadDto(this Order order) =>
        new(
            order.Id,
            order.OrderDate.GetValueOrDefault(),
            order.ShippedDate,
            order.ShippingAddress,
            order.OrderStatus,
            order.CustomerId.GetValueOrDefault(),
            order.Invoice?.ToReadDto(),
            order.OrderItems.Select(oi => oi.ToReadDto()).ToList()
        );

    public static OrderItemReadDto ToReadDto(this OrderItem orderItem) =>
        new(orderItem.ItemId, orderItem.OrderItemQuantity);

    public static InvoiceReadDto ToReadDto(this Invoice invoice) =>
        new(
            invoice.Id,
            invoice.InvoiceDate.GetValueOrDefault(),
            invoice.OrderId,
            invoice.InvoiceStatus,
            invoice.TotalAmount.GetValueOrDefault()
        );

    public static OrderFullReadDto ToFullReadDto(this Order order) =>
        order.ToFullReadDto(order.Invoice);

    public static OrderFullReadDto ToFullReadDto(this Invoice invoice) =>
        invoice.Order.ToFullReadDto(invoice);

    private static OrderFullReadDto ToFullReadDto(this Order order, Invoice? invoice) =>
        new()
        {
            Id = order.Id,
            OrderDate = order.OrderDate,
            ShippedDate = order.ShippedDate,
            ShippingAddress = order.ShippingAddress,
            OrderStatus = order.OrderStatus,
            Customer = order.Customer?.ToReadDto(),
            Invoice = invoice?.ToReadDto(),
            Items = order
                .OrderItems.Select(oi => new ItemReadDto
                {
                    ExternalBatchId = oi.ItemId,
                    ExternalProductId = oi.Item.ExternalProductId,
                    Name = oi.Item.Name,
                    Price = oi.Item.Price,
                    Lot = oi.Item.Lot,
                    ExpirationDate = oi.Item.ExpirationDate,
                    Quantity = oi.OrderItemQuantity,
                })
                .ToList(),
        };

    public static Order ToEntity(this OrderCreateDto dto) =>
        new()
        {
            OrderDate = dto.OrderDate,
            ShippedDate = dto.ShippedDate,
            ShippingAddress = dto.ShippingAddress,
            OrderStatus = dto.OrderStatus,
            CustomerId = dto.CustomerId,
            OrderItems = dto
                .OrderItems.Select(oi => new OrderItem
                {
                    ItemId = oi.ItemId,
                    OrderItemQuantity = oi.OrderItemQuantity,
                })
                .ToList(),
        };

    public static Order ToEntity(this OrderUpdateDto dto, Guid id) =>
        new()
        {
            Id = id,
            OrderDate = dto.OrderDate,
            ShippedDate = dto.ShippedDate,
            ShippingAddress = dto.ShippingAddress,
            OrderStatus = dto.OrderStatus,
            CustomerId = dto.CustomerId,
            OrderItems =
                dto.OrderItems?.Select(oi => new OrderItem
                    {
                        ItemId = oi.ItemId,
                        OrderItemQuantity = oi.OrderItemQuantity,
                    })
                    .ToList()
                ?? [],
        };

    public static Invoice ToEntity(this InvoiceCreateDto dto) =>
        new()
        {
            Id = dto.ID ?? 0,
            InvoiceDate = dto.InvoiceDate,
            InvoiceStatus = dto.InvoiceStatus,
            TotalAmount = dto.TotalAmount,
        };

    public static Invoice ToEntity(this InvoiceUpdateDto dto, int id) =>
        new()
        {
            Id = id,
            InvoiceDate = dto.InvoiceDate,
            InvoiceStatus = dto.InvoiceStatus,
            TotalAmount = dto.TotalAmount,
        };

    public static List<OrderItemPublishedDto> ToPublishedDtos(
        this IEnumerable<OrderItem> orderItems
    ) =>
        orderItems
            .Select(oi => new OrderItemPublishedDto(oi.ItemId, oi.OrderItemQuantity))
            .ToList();

    public static List<OrderItemPublishedDto> ToPublishedDtos(
        this IEnumerable<OrderItemUpdateDto> orderItems
    ) =>
        orderItems
            .Select(oi => new OrderItemPublishedDto(oi.ItemId, oi.OrderItemQuantity))
            .ToList();
}
