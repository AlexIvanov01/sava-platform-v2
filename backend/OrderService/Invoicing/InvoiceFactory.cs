using System;
using System.Globalization;
using System.Linq;
using OrderService.Models;

namespace OrderService.Invoicing;

public static class InvoiceFactory
{
    public const int DefaultPaymentTermsDays = 14;

    // The invoice for a completed order: number, dates and payment fields get their defaults, and
    // supplier, customer, items and totals are copied so later changes don't reach it.
    public static Invoice Create(Order order, CompanySettings company, string number, DateTime now)
    {
        var items = order
            .Items.OrderBy(i => i.Name)
            .ThenBy(i => i.ProductId)
            .Select(i => new InvoiceItem
            {
                ProductId = i.ProductId,
                SKU = i.SKU,
                Name = i.Name,
                UnitOfMeasure = i.UnitOfMeasure,
                Quantity = i.Quantity,
                UnitPrice = i.UnitPrice ?? 0m,
                VatRate = i.VatRate ?? 0m,
                Net = i.Net(),
                Vat = i.Vat(),
                Gross = i.Gross(),
            })
            .ToList();

        return new Invoice
        {
            OrderId = order.Id,
            Order = order,
            Number = number,
            InvoiceDate = now,
            TaxEventDate = now,
            DueDate = now.Date.AddDays(company.PaymentTermsDays ?? DefaultPaymentTermsDays),
            Supplier = company.ToSupplier(),
            Customer = order.Customer?.Copy() ?? new CustomerSnapshot(),
            Items = items,
            VatTotals = items
                .GroupBy(i => i.VatRate)
                .OrderBy(g => g.Key)
                .Select(g => new InvoiceVatTotal
                {
                    VatRate = g.Key,
                    Net = g.Sum(i => i.Net),
                    Vat = g.Sum(i => i.Vat),
                })
                .ToList(),
            Net = items.Sum(i => i.Net),
            Vat = items.Sum(i => i.Vat),
            Gross = items.Sum(i => i.Gross),
        };
    }

    // One more than the highest number (all numbers have 10 digits, so they sort as text).
    public static string NextNumber(string? highest) =>
        ((highest is { Length: > 0 } && long.TryParse(highest, out var n) ? n : 0) + 1).ToString(
            "D10",
            CultureInfo.InvariantCulture
        );

    private static SupplierSnapshot ToSupplier(this CompanySettings company) =>
        new()
        {
            Name = company.Name,
            UIC = company.UIC,
            VATNumber = company.VATNumber,
            Address = company.Address,
            City = company.City,
            Country = company.Country,
            Manager = company.Manager,
            BankName = company.BankName,
            IBAN = company.IBAN,
            BIC = company.BIC,
            Email = company.Email,
            PhoneNumber = company.PhoneNumber,
        };
}
