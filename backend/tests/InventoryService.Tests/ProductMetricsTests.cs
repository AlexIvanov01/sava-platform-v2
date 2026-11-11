using InventoryService.Mappings;
using InventoryService.Models;
using Microsoft.Extensions.Time.Testing;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

public class ProductMetricsTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(TestData.Date(2026, 2028));

    private static ProductBatch Batch(int quantity, decimal purchasePrice, int expiresInDays)
    {
        var batch = InventoryBuilder.Batch(quantity: quantity);
        batch.PurchasePrice = purchasePrice;
        batch.ExpirationDate = Today.AddDays(expiresInDays).ToDateTime(TimeOnly.MinValue);
        return batch;
    }

    [Fact]
    public void Cost_IsHighestPurchasePriceOfActiveBatches()
    {
        var product = InventoryBuilder.Product(
            null,
            Batch(10, 3.10m, 30),
            Batch(5, 3.40m, 60),
            Batch(0, 9.99m, 90), // sold out
            Batch(-2, 8.00m, 90), // oversold
            Batch(20, 7.50m, -1) // expired yesterday
        );

        product.Cost(Today).ShouldBe(3.40m);
    }

    [Fact]
    public void Cost_NoActiveBatches_IsNull()
    {
        var product = InventoryBuilder.Product(null, Batch(0, 3m, 30), Batch(10, 4m, -5));

        product.Cost(Today).ShouldBeNull();
        InventoryBuilder.Product().Cost(Today).ShouldBeNull();
    }

    [Fact]
    public void AvailableQuantity_SumsNonExpiredBatchesIncludingNegative()
    {
        var product = InventoryBuilder.Product(
            null,
            Batch(10, 1m, 30),
            Batch(-3, 1m, 30),
            Batch(7, 1m, 0), // expires today: still sellable
            Batch(50, 1m, -1) // expired
        );

        product.AvailableQuantity(Today).ShouldBe(14);
    }

    [Fact]
    public void BatchWithoutExpirationDate_NeverExpires()
    {
        var batch = Batch(4, 2.5m, 0);
        batch.ExpirationDate = null;

        batch.IsExpired(Today).ShouldBeFalse();
        batch.IsActive(Today).ShouldBeTrue();
        InventoryBuilder.Product(null, batch).AvailableQuantity(Today).ShouldBe(4);
    }

    [Theory]
    [InlineData(10.00, 7.50, 2.50, 25.00)]
    [InlineData(4.89, 3.20, 1.69, 34.56)]
    [InlineData(3.00, 3.40, -0.40, -13.33)]
    public void Margin_EbitAndPercentOfPrice(double price, double cost, double ebit, double percent)
    {
        Margin.Ebit((decimal)price, (decimal)cost).ShouldBe((decimal)ebit);
        Margin.EbitPercent((decimal)price, (decimal)cost).ShouldBe((decimal)percent);
    }

    [Theory]
    [InlineData(null, 3.0)]
    [InlineData(10.0, null)]
    [InlineData(0.0, 3.0)]
    public void Margin_MissingPriceOrCost_IsNull(double? price, double? cost)
    {
        Margin.EbitPercent((decimal?)price, (decimal?)cost).ShouldBeNull();
        if (price == null || cost == null)
        {
            Margin.Ebit((decimal?)price, (decimal?)cost).ShouldBeNull();
        }
    }

    [Fact]
    public void ToReadDto_IncludesDerivedFigures()
    {
        var product = InventoryBuilder.Product(null, Batch(10, 3.10m, 30), Batch(5, 3.40m, 60));
        product.ListPrice = 4.89m;

        var dto = product.ToReadDto(Today);

        dto.Cost.ShouldBe(3.40m);
        dto.AvailableQuantity.ShouldBe(15);
        dto.Ebit.ShouldBe(1.49m);
        dto.EbitPercent.ShouldBe(30.47m);
    }

    [Fact]
    public void ToReadDto_UsesTheGivenDayForExpiry()
    {
        var clock = new FakeTimeProvider(
            new DateTimeOffset(Today.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero)
        );
        var product = InventoryBuilder.Product(null, Batch(10, 3m, 1));
        DateOnly Now() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

        product.ToReadDto(Now()).AvailableQuantity.ShouldBe(10);
        clock.Advance(TimeSpan.FromDays(2));
        var later = product.ToReadDto(Now());

        later.AvailableQuantity.ShouldBe(0);
        later.Cost.ShouldBeNull();
        later.Ebit.ShouldBeNull();
    }
}
