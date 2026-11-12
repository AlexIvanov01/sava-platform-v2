using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace OrderService.Dtos;

[JsonConverter(typeof(JsonStringEnumConverter<SalesGrouping>))]
public enum SalesGrouping
{
    Customer,
    Product,
    Month,
}

// Key: customer id, product id or "yyyy-MM". Quantity only when grouped by product. Ebit and
// EbitPercent cover only items with a known cost; NetWithoutCost is the revenue left out.
public record SalesReportRowDto(
    string Key,
    string? Label,
    int? Quantity,
    int OrderCount,
    decimal Net,
    decimal Vat,
    decimal Gross,
    decimal Cost,
    decimal Ebit,
    decimal? EbitPercent,
    decimal NetWithoutCost
);

public record SalesReportDto(
    SalesGrouping GroupBy,
    DateOnly? From,
    DateOnly? To,
    List<SalesReportRowDto> Rows,
    SalesReportRowDto Totals
);
