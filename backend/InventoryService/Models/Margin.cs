using System;

namespace InventoryService.Models;

// EBIT per unit and its margin on price. OrderService computes the same figures per order item.
public static class Margin
{
    public static decimal? Ebit(decimal? price, decimal? cost) => price - cost;

    // Percent of the price, rounded to 2 decimals; null when the price is missing or zero.
    public static decimal? EbitPercent(decimal? price, decimal? cost) =>
        price is null or 0m || cost is null
            ? null
            : Math.Round(
                (price.Value - cost.Value) / price.Value * 100m,
                2,
                MidpointRounding.AwayFromZero
            );
}
