using OrderService.Dtos;
using OrderService.Mappings;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class InvoiceMappingsTests
{
    private static Invoice PaidInvoice(Order order)
    {
        var f = TestData.Faker;
        var invoice = OrderBuilder.Invoice(order);
        invoice.PaymentMethod = PaymentMethod.Card;
        invoice.PaymentStatus = PaymentStatus.Paid;
        invoice.PaidDate = TestData.Date(2025, 2026);
        invoice.VatExemptionReason = f.Lorem.Sentence();
        invoice.CreatedAt = TestData.Date(2024, 2025);
        invoice.CreatedBy = f.Internet.UserName();
        invoice.UpdatedAt = TestData.Date(2025, 2026);
        invoice.UpdatedBy = f.Internet.UserName();
        return invoice;
    }

    [Fact]
    public void ToReadDto_MapsEveryField()
    {
        var order = OrderBuilder.Order(OrderBuilder.Item(), OrderBuilder.Item(batchCount: 2));
        order.Status = OrderStatus.Cancelled;
        var invoice = PaidInvoice(order);
        var today = DateOnly.FromDateTime(TestData.Date(2025, 2026));

        var dto = invoice.ToReadDto(today);

        dto.Id.ShouldBe(invoice.Id);
        dto.OrderId.ShouldBe(order.Id);
        dto.OrderStatus.ShouldBe(OrderStatus.Cancelled);
        dto.Number.ShouldBe(invoice.Number);
        dto.InvoiceDate.ShouldBe(invoice.InvoiceDate);
        dto.TaxEventDate.ShouldBe(invoice.TaxEventDate);
        dto.DueDate.ShouldBe(invoice.DueDate);
        dto.PaymentMethod.ShouldBe(PaymentMethod.Card);
        dto.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        dto.PaidDate.ShouldBe(invoice.PaidDate);
        dto.IsOverdue.ShouldBeFalse();
        dto.VatExemptionReason.ShouldBe(invoice.VatExemptionReason);
        dto.Supplier.ShouldBe(invoice.Supplier.ToReadDto());
        dto.Customer.ShouldBe(invoice.Customer.ToReadDto());
        dto.Items.ShouldBe([
            .. invoice.Items.Select(i => new InvoiceItemReadDto(
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
            )),
        ]);
        dto.VatTotals.ShouldBe([
            .. invoice.VatTotals.Select(t => new InvoiceVatTotalReadDto(t.VatRate, t.Net, t.Vat)),
        ]);
        (dto.Net, dto.Vat, dto.Gross).ShouldBe((invoice.Net, invoice.Vat, invoice.Gross));
        dto.Audit.ShouldBe(
            new AuditReadDto(
                invoice.CreatedAt,
                invoice.UpdatedAt,
                invoice.CreatedBy,
                invoice.UpdatedBy
            )
        );
    }

    [Fact]
    public void ToReadDto_IsOverdueDependsOnToday()
    {
        var invoice = OrderBuilder.Invoice(OrderBuilder.Order(OrderBuilder.Item()));
        var due = DateOnly.FromDateTime(invoice.DueDate!.Value);

        invoice.ToReadDto(due).IsOverdue.ShouldBeFalse();
        invoice.ToReadDto(due.AddDays(1)).IsOverdue.ShouldBeTrue();
    }

    [Fact]
    public void ToReadDto_SortsVatTotalsByRate()
    {
        var invoice = OrderBuilder.Invoice(OrderBuilder.Order(OrderBuilder.Item()));
        var f = TestData.Faker;
        invoice.VatTotals =
        [
            new() { VatRate = 20m, Net = f.Finance.Amount() },
            new() { VatRate = 0m, Net = f.Finance.Amount() },
            new() { VatRate = 9m, Net = f.Finance.Amount() },
        ];

        invoice
            .ToReadDto(DateOnly.FromDateTime(TestData.Date(2025, 2026)))
            .VatTotals.Select(t => t.VatRate)
            .ShouldBe([0m, 9m, 20m]);
    }

    [Fact]
    public void ToReadDto_WithoutItsOrder_HasNoOrderStatus()
    {
        var invoice = OrderBuilder.Invoice(OrderBuilder.Order(OrderBuilder.Item()));
        invoice.Order = null;

        var dto = invoice.ToReadDto(DateOnly.FromDateTime(TestData.Date(2025, 2026)));

        dto.OrderStatus.ShouldBeNull();
        dto.OrderId.ShouldBe(invoice.OrderId);
    }

    [Fact]
    public void ToSummaryDto_MapsTheInvoiceAsShownOnItsOrder()
    {
        var invoice = PaidInvoice(OrderBuilder.Order(OrderBuilder.Item()));

        invoice
            .ToSummaryDto()
            .ShouldBe(
                new InvoiceSummaryReadDto(
                    invoice.Id,
                    invoice.Number,
                    invoice.InvoiceDate,
                    invoice.DueDate,
                    PaymentStatus.Paid,
                    invoice.Gross
                )
            );
    }
}
