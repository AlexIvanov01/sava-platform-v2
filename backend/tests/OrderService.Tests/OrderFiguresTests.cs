using OrderService.Models;

namespace OrderService.Tests;

public class OrderFiguresTests
{
    private static OrderItem Item(
        decimal? unitPrice,
        decimal? vatRate,
        decimal? cost,
        params int[] quantities
    ) =>
        new()
        {
            UnitPrice = unitPrice,
            VatRate = vatRate,
            Cost = cost,
            Batches = [.. quantities.Select(q => new OrderItemBatch { Quantity = q })],
        };

    [Fact]
    public void Quantity_IsTheSumOfTheBatches()
    {
        Item(1m, 20m, 1m, 3, 4, 5).Quantity.ShouldBe(12);
        Item(1m, 20m, 1m).Quantity.ShouldBe(0);
    }

    // Rounded to cents half away from zero (banker's rounding would give 0.12 for 0.125).
    [Theory]
    [InlineData(0.125, 1, 0.13)]
    [InlineData(0.333, 3, 1.00)]
    [InlineData(2.50, 4, 10.00)]
    [InlineData(1.005, 1, 1.01)]
    public void Net_IsUnitPriceTimesQuantityRoundedToCents(
        double unitPrice,
        int quantity,
        double expected
    )
    {
        Item((decimal)unitPrice, 20m, null, quantity).Net().ShouldBe((decimal)expected);
    }

    [Theory]
    [InlineData(1.25, 10, 0.13, 1.38)]
    [InlineData(10.05, 20, 2.01, 12.06)]
    [InlineData(7.00, 9, 0.63, 7.63)]
    [InlineData(7.00, 0, 0.00, 7.00)]
    public void VatAndGross_AreRoundedPerItem(
        double unitPrice,
        double vatRate,
        double expectedVat,
        double expectedGross
    )
    {
        var item = Item((decimal)unitPrice, (decimal)vatRate, null, 1);

        item.Vat().ShouldBe((decimal)expectedVat);
        item.Gross().ShouldBe((decimal)expectedGross);
    }

    [Fact]
    public void MissingVatRateOrUnitPrice_CountsAsZero()
    {
        var noVat = Item(10m, null, null, 2);
        noVat.Vat().ShouldBe(0m);
        noVat.Gross().ShouldBe(20m);

        var noPrice = Item(null, 20m, 1m, 2);
        noPrice.Net().ShouldBe(0m);
        noPrice.Vat().ShouldBe(0m);
        noPrice.Ebit().ShouldBe(-2m);
        noPrice.EbitPercent().ShouldBeNull();
    }

    [Fact]
    public void ItemCostAndEbit_UseTheUnitCostTimesQuantity()
    {
        var item = Item(10m, 20m, 7.5m, 2, 2);

        item.TotalCost().ShouldBe(30m);
        item.Ebit().ShouldBe(10m);
        item.EbitPercent().ShouldBe(25m);
    }

    [Fact]
    public void ItemWithoutCost_HasNoCostOrEbit()
    {
        var item = Item(10m, 20m, null, 2);

        item.TotalCost().ShouldBeNull();
        item.Ebit().ShouldBeNull();
        item.EbitPercent().ShouldBeNull();
    }

    // Totals are sums of the rounded item amounts, not rounded sums.
    [Fact]
    public void Totals_SumTheRoundedItemAmounts()
    {
        OrderItem[] items = [Item(0.125m, 10m, 0.0625m, 1), Item(0.125m, 10m, 0.0625m, 1)];

        items.Net().ShouldBe(0.26m);
        items.Vat().ShouldBe(0.02m);
        items.Gross().ShouldBe(0.28m);
        items.TotalCost().ShouldBe(0.12m);
    }

    [Fact]
    public void TotalCost_IsNullWhenAnyItemHasNoCost()
    {
        OrderItem[] items = [Item(10m, 20m, 5m, 1), Item(10m, 20m, null, 1)];

        items.TotalCost().ShouldBeNull();
        items.Net().ShouldBe(20m);
    }

    [Fact]
    public void Totals_OfNoItems_AreZero()
    {
        OrderItem[] items = [];

        items.Net().ShouldBe(0m);
        items.Vat().ShouldBe(0m);
        items.Gross().ShouldBe(0m);
        items.TotalCost().ShouldBe(0m);
    }

    [Theory]
    [InlineData(10, 7.5, 25.00)]
    [InlineData(3, 2, 33.33)]
    [InlineData(3, 1, 66.67)]
    [InlineData(10, 12, -20.00)]
    [InlineData(10, 0, 100.00)]
    public void EbitPercent_IsTheMarginOnNetRoundedToTwoDecimals(
        double net,
        double cost,
        double expected
    )
    {
        OrderFigures.EbitPercent((decimal)net, (decimal)cost).ShouldBe((decimal)expected);
    }

    [Fact]
    public void EbitPercent_IsNullWithoutNetOrCost()
    {
        OrderFigures.EbitPercent(0m, 5m).ShouldBeNull();
        OrderFigures.EbitPercent(10m, null).ShouldBeNull();
    }
}
