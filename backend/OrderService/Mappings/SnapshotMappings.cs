using System;
using System.Globalization;
using CustomerService;
using InventoryService;
using OrderService.Models;
using OrderService.SyncDataServices.Grpc;

namespace OrderService.Mappings;

// Protobuf sends missing values as empty strings and decimals/dates as invariant strings.
public static class SnapshotMappings
{
    public static ProductSnapshot ToSnapshot(this GrpcProductSnapshot model) =>
        new(
            Guid.Parse(model.Id),
            NullIfEmpty(model.Sku),
            NullIfEmpty(model.Name),
            NullIfEmpty(model.UnitOfMeasure),
            ParseDecimal(model.ListPrice),
            ParseDecimal(model.VatRate),
            ParseDecimal(model.Cost)
        );

    public static BatchSnapshot ToSnapshot(this GrpcBatchSnapshot model) =>
        new(
            Guid.Parse(model.Id),
            Guid.Parse(model.ProductId),
            NullIfEmpty(model.Lot),
            string.IsNullOrEmpty(model.ExpirationDate)
                ? null
                : DateTime.Parse(
                    model.ExpirationDate,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind
                )
        );

    public static CustomerSnapshot ToSnapshot(this GrpcCustomerModel model) =>
        new()
        {
            Name = NullIfEmpty(model.Name),
            CompanyName = NullIfEmpty(model.CompanyName),
            Email = NullIfEmpty(model.Email),
            PhoneNumber = NullIfEmpty(model.PhoneNumber),
            Address = NullIfEmpty(model.Address),
            City = NullIfEmpty(model.City),
            Country = NullIfEmpty(model.Country),
            BankName = NullIfEmpty(model.BankName),
            IBAN = NullIfEmpty(model.IBAN),
            BIC = NullIfEmpty(model.BIC),
            VATNumber = NullIfEmpty(model.VATNumber),
            UIC = NullIfEmpty(model.UIC),
        };

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static decimal? ParseDecimal(string value) =>
        value.Length == 0 ? null : decimal.Parse(value, CultureInfo.InvariantCulture);
}
