using System.Collections.Generic;
using System.Linq;
using OrderService.Dtos;
using OrderService.Models;

namespace OrderService.Mappings;

public static class OrderMappings
{
    public static OrderReadDto ToReadDto(this Order order) => order.ToReadDto(order.Invoice);

    // An invoice with its order; the invoice is passed in because untracked queries don't always
    // set the order's back-reference.
    public static OrderReadDto ToOrderReadDto(this Invoice invoice) =>
        invoice.Order.ToReadDto(invoice);

    private static OrderReadDto ToReadDto(this Order order, Invoice? invoice)
    {
        var cost = order.Items.TotalCost();
        var net = order.Items.Net();
        return new(
            order.Id,
            order.OrderDate,
            order.ShippedDate,
            order.ShippingAddress,
            order.OrderStatus,
            order.CustomerId,
            order.Customer?.ToReadDto(),
            invoice?.ToReadDto(),
            order.Items.OrderBy(l => l.Name).ThenBy(l => l.ProductId).Select(ToReadDto).ToList(),
            new OrderTotalsReadDto(
                net,
                order.Items.Vat(),
                order.Items.Gross(),
                cost,
                net - cost,
                OrderFigures.EbitPercent(net, cost)
            ),
            order.ToAuditDto()
        );
    }

    public static OrderItemReadDto ToReadDto(this OrderItem item) =>
        new(
            item.ProductId,
            item.SKU,
            item.Name,
            item.UnitOfMeasure,
            item.Quantity,
            item.ListPrice,
            item.UnitPrice,
            item.VatRate,
            item.Cost,
            item.Net(),
            item.Vat(),
            item.Gross(),
            item.Ebit(),
            item.EbitPercent(),
            item.Batches.OrderBy(b => b.ExpirationDate)
                .ThenBy(b => b.BatchId)
                .Select(b => new OrderItemBatchReadDto(
                    b.BatchId,
                    b.Lot,
                    b.ExpirationDate,
                    b.Quantity
                ))
                .ToList()
        );

    public static CustomerSnapshotReadDto ToReadDto(this CustomerSnapshot customer) =>
        new(
            customer.Name,
            customer.CompanyName,
            customer.Email,
            customer.PhoneNumber,
            customer.Address,
            customer.City,
            customer.Country,
            customer.BankName,
            customer.IBAN,
            customer.BIC,
            customer.VATNumber,
            customer.UIC
        );

    public static InvoiceReadDto ToReadDto(this Invoice invoice) =>
        new(
            invoice.Id,
            invoice.InvoiceDate.GetValueOrDefault(),
            invoice.OrderId,
            invoice.InvoiceStatus,
            invoice.TotalAmount.GetValueOrDefault(),
            invoice.ToAuditDto()
        );

    public static AuditReadDto ToAuditDto(this AuditableEntity entity) =>
        new(entity.CreatedAt, entity.UpdatedAt, entity.CreatedBy, entity.UpdatedBy);

    // Items are added separately (Ordering/OrderItems), because they need snapshots.
    public static Order ToEntity(this OrderCreateDto dto) =>
        new()
        {
            OrderDate = dto.OrderDate,
            ShippedDate = dto.ShippedDate,
            ShippingAddress = dto.ShippingAddress,
            OrderStatus = dto.OrderStatus,
            CustomerId = dto.CustomerId,
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

    public static List<OrderItemPublishedDto> ToPublishedDtos(this IEnumerable<OrderItem> items) =>
        items
            .Select(l => new OrderItemPublishedDto(
                l.ProductId,
                l.Batches.Select(b => new OrderItemBatchPublishedDto(b.BatchId, b.Quantity))
                    .ToList()
            ))
            .ToList();
}
