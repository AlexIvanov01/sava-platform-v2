using System;
using System.Linq;
using OrderService.Dtos;
using OrderService.Models;

namespace OrderService.Mappings;

public static class InvoiceMappings
{
    public static InvoiceSummaryReadDto ToSummaryDto(this Invoice invoice) =>
        new(
            invoice.Id,
            invoice.Number,
            invoice.InvoiceDate,
            invoice.DueDate,
            invoice.PaymentStatus,
            invoice.Gross
        );

    public static InvoiceReadDto ToReadDto(this Invoice invoice, DateOnly today) =>
        new(
            invoice.Id,
            invoice.OrderId,
            invoice.Order?.Status,
            invoice.Number,
            invoice.InvoiceDate,
            invoice.TaxEventDate,
            invoice.DueDate,
            invoice.PaymentMethod,
            invoice.PaymentStatus,
            invoice.PaidDate,
            invoice.IsOverdue(today),
            invoice.VatExemptionReason,
            invoice.Supplier.ToReadDto(),
            invoice.Customer.ToReadDto(),
            invoice
                .Items.Select(i => new InvoiceItemReadDto(
                    i.ProductId,
                    i.SKU,
                    i.Name,
                    i.UnitOfMeasure,
                    i.Quantity,
                    i.UnitPrice,
                    i.VatRate,
                    i.Net,
                    i.Vat,
                    i.Gross
                ))
                .ToList(),
            invoice
                .VatTotals.OrderBy(t => t.VatRate)
                .Select(t => new InvoiceVatTotalReadDto(t.VatRate, t.Net, t.Vat))
                .ToList(),
            invoice.Net,
            invoice.Vat,
            invoice.Gross,
            invoice.ToAuditDto()
        );

    public static SupplierReadDto ToReadDto(this SupplierSnapshot supplier) =>
        new(
            supplier.Name,
            supplier.UIC,
            supplier.VATNumber,
            supplier.Address,
            supplier.City,
            supplier.Country,
            supplier.Manager,
            supplier.BankName,
            supplier.IBAN,
            supplier.BIC,
            supplier.Email,
            supplier.PhoneNumber
        );
}
