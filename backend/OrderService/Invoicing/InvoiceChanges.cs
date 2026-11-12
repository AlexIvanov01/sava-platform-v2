using System;
using OrderService.Dtos;
using OrderService.Models;

namespace OrderService.Invoicing;

public static class InvoiceChanges
{
    // Applies the editable fields of a PUT (null = unchanged). Marking the invoice Paid sets
    // PaidDate to today unless one is given; marking it Unpaid clears it. Returns why the change
    // is invalid, or null.
    public static string? Apply(Invoice invoice, InvoiceUpdateDto dto, DateTime today)
    {
        var status = dto.PaymentStatus ?? invoice.PaymentStatus;
        if (status == PaymentStatus.Unpaid && dto.PaidDate != null)
        {
            return "PaidDate can only be set on a Paid invoice.";
        }

        invoice.Number = dto.Number ?? invoice.Number;
        invoice.InvoiceDate = dto.InvoiceDate ?? invoice.InvoiceDate;
        invoice.TaxEventDate = dto.TaxEventDate ?? invoice.TaxEventDate;
        invoice.DueDate = dto.DueDate ?? invoice.DueDate;
        invoice.PaymentMethod = dto.PaymentMethod ?? invoice.PaymentMethod;
        invoice.VatExemptionReason = dto.VatExemptionReason ?? invoice.VatExemptionReason;
        invoice.PaymentStatus = status;
        invoice.PaidDate =
            status == PaymentStatus.Paid ? dto.PaidDate ?? invoice.PaidDate ?? today.Date : null;

        return null;
    }
}
