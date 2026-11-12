using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OrderService.Dtos;
using OrderService.Models;

namespace OrderService.Reporting;

// Sales and EBIT from completed orders. Revenue comes from the frozen invoice items; cost from
// the cost each order item copied when it was added (Cost x quantity, rounded to cents).
public static class SalesReport
{
    // Expects the orders' invoices and items to be loaded. productId keeps only that product's
    // invoice items.
    public static SalesReportDto Build(
        IEnumerable<Order> orders,
        SalesGrouping groupBy,
        DateOnly? from,
        DateOnly? to,
        Guid? productId
    )
    {
        var sales = orders
            .Where(o => o.Invoice != null)
            .SelectMany(o =>
                o.Invoice!.Items.Where(i => productId == null || i.ProductId == productId)
                    .Select(i => new Sale(
                        o,
                        i,
                        o.Items.FirstOrDefault(oi => oi.ProductId == i.ProductId)?.TotalCost()
                    ))
            )
            .ToList();

        var rows = (
            groupBy switch
            {
                SalesGrouping.Product => sales
                    .GroupBy(s => s.Item.ProductId.ToString())
                    .Select(g => Row(g.Key, ProductLabel(Latest(g).Item), g, withQuantity: true))
                    .OrderByDescending(r => r.Net),
                SalesGrouping.Month => sales
                    .GroupBy(s =>
                        s.Order.Invoice!.InvoiceDate?.ToString(
                            "yyyy-MM",
                            CultureInfo.InvariantCulture
                        ) ?? string.Empty
                    )
                    .Select(g => Row(g.Key, g.Key, g, withQuantity: false))
                    .OrderBy(r => r.Key, StringComparer.Ordinal),
                _ => sales
                    .GroupBy(s => s.Order.CustomerId?.ToString() ?? string.Empty)
                    .Select(g =>
                        Row(g.Key, CustomerLabel(Latest(g).Order.Invoice!), g, withQuantity: false)
                    )
                    .OrderByDescending(r => r.Net),
            }
        ).ToList();

        return new SalesReportDto(
            groupBy,
            from,
            to,
            rows,
            Row("total", null, sales, withQuantity: groupBy == SalesGrouping.Product)
        );
    }

    private static SalesReportRowDto Row(
        string key,
        string? label,
        IEnumerable<Sale> group,
        bool withQuantity
    )
    {
        var sales = group.ToList();
        var costed = sales.Where(s => s.Cost != null).ToList();
        var net = sales.Sum(s => s.Item.Net);
        var costedNet = costed.Sum(s => s.Item.Net);
        var cost = costed.Sum(s => s.Cost!.Value);
        return new SalesReportRowDto(
            key,
            label,
            withQuantity ? sales.Sum(s => s.Item.Quantity) : null,
            sales.Select(s => s.Order.Id).Distinct().Count(),
            net,
            sales.Sum(s => s.Item.Vat),
            sales.Sum(s => s.Item.Gross),
            cost,
            costedNet - cost,
            OrderFigures.EbitPercent(costedNet, costed.Count == 0 ? null : cost),
            net - costedNet
        );
    }

    // Labels come from the most recent invoice, as names can differ between snapshots.
    private static Sale Latest(IEnumerable<Sale> group) =>
        group.MaxBy(s => s.Order.Invoice!.InvoiceDate)!;

    private static string? CustomerLabel(Invoice invoice) =>
        invoice.Customer.CompanyName ?? invoice.Customer.Name;

    private static string? ProductLabel(InvoiceItem item) =>
        item.SKU == null ? item.Name : $"{item.Name} ({item.SKU})";

    private sealed record Sale(Order Order, InvoiceItem Item, decimal? Cost);
}
