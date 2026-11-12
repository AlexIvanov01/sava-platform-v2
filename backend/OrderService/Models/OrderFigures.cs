using System;
using System.Collections.Generic;
using System.Linq;

namespace OrderService.Models;

// Money figures computed on read, never stored. Amounts are EUR rounded to cents per item; order
// totals are sums of the rounded item amounts. EBIT is net revenue minus cost, and its percent is
// the margin on net revenue (InventoryService's Margin uses the same formula per unit).
public static class OrderFigures
{
    public static decimal Net(this OrderItem item) => Round((item.UnitPrice ?? 0m) * item.Quantity);

    public static decimal Net(this IEnumerable<OrderItem> items) => items.Sum(l => l.Net());

    public static decimal Vat(this OrderItem item) =>
        Round(item.Net() * (item.VatRate ?? 0m) / 100m);

    public static decimal Vat(this IEnumerable<OrderItem> items) => items.Sum(l => l.Vat());

    public static decimal Gross(this OrderItem item) => item.Net() + item.Vat();

    public static decimal Gross(this IEnumerable<OrderItem> items) => items.Sum(l => l.Gross());

    // Null when the product had no cost when it was added.
    public static decimal? TotalCost(this OrderItem item) =>
        item.Cost is { } cost ? Round(cost * item.Quantity) : null;

    // Null when any item has no cost, since the total would be understated.
    public static decimal? TotalCost(this IEnumerable<OrderItem> items)
    {
        var costs = items.Select(l => l.TotalCost()).ToList();
        return costs.Contains(null) ? null : costs.Sum();
    }

    public static decimal? Ebit(this OrderItem item) => item.Net() - item.TotalCost();

    public static decimal? EbitPercent(this OrderItem item) =>
        EbitPercent(item.Net(), item.TotalCost());

    // Percent of net revenue, rounded to 2 decimals; null when it is zero or the cost is unknown.
    public static decimal? EbitPercent(decimal net, decimal? cost) =>
        net == 0m || cost is null
            ? null
            : Math.Round((net - cost.Value) / net * 100m, 2, MidpointRounding.AwayFromZero);

    private static decimal Round(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
