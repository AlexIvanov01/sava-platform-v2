using OrderService.Dtos;
using OrderService.Invoicing;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class InvoiceChangesTests
{
    private static readonly InvoiceUpdateDto NoChanges = new(
        null,
        null,
        null,
        null,
        null,
        null,
        null,
        null
    );

    private DateTime Today { get; } = TestData.Date(2026, 2027);

    private static Invoice NewInvoice() =>
        OrderBuilder.Invoice(OrderBuilder.Order(OrderBuilder.Item()));

    private static Invoice PaidInvoice()
    {
        var invoice = NewInvoice();
        invoice.PaymentStatus = PaymentStatus.Paid;
        invoice.PaidDate = TestData.Date(2025, 2026);
        invoice.PaymentMethod = PaymentMethod.Card;
        return invoice;
    }

    // Every field InvoiceChanges may change.
    private static object Editable(Invoice i) =>
        (
            i.Number,
            i.InvoiceDate,
            i.TaxEventDate,
            i.DueDate,
            i.PaymentMethod,
            i.PaymentStatus,
            i.PaidDate,
            i.VatExemptionReason
        );

    // What it must never change.
    private static object Frozen(Invoice i) =>
        (i.Id, i.OrderId, i.Supplier, i.Customer, i.Items, i.VatTotals, i.Net, i.Vat, i.Gross);

    [Fact]
    public void Apply_NoChanges_KeepsEverything()
    {
        foreach (var invoice in new[] { NewInvoice(), PaidInvoice() })
        {
            var (editable, frozen) = (Editable(invoice), Frozen(invoice));

            InvoiceChanges.Apply(invoice, NoChanges, Today).ShouldBeNull();

            Editable(invoice).ShouldBe(editable);
            Frozen(invoice).ShouldBe(frozen);
        }
    }

    [Fact]
    public void Apply_GivenFields_ChangeOnlyThose()
    {
        var invoice = NewInvoice();
        var before = invoice.InvoiceDate;
        var dto = NoChanges with
        {
            Number = OrderBuilder.InvoiceNumber(),
            TaxEventDate = TestData.Date(2025, 2026),
            DueDate = TestData.Date(2026, 2027),
            PaymentMethod = PaymentMethod.Cash,
            VatExemptionReason = TestData.Faker.Lorem.Sentence(),
        };

        InvoiceChanges.Apply(invoice, dto, Today).ShouldBeNull();

        invoice.Number.ShouldBe(dto.Number);
        invoice.TaxEventDate.ShouldBe(dto.TaxEventDate);
        invoice.DueDate.ShouldBe(dto.DueDate);
        invoice.PaymentMethod.ShouldBe(PaymentMethod.Cash);
        invoice.VatExemptionReason.ShouldBe(dto.VatExemptionReason);
        invoice.InvoiceDate.ShouldBe(before);
        invoice.PaymentStatus.ShouldBe(PaymentStatus.Unpaid);
        invoice.PaidDate.ShouldBeNull();
    }

    [Fact]
    public void Apply_InvoiceDate_DoesNotMoveTheOtherDates()
    {
        var invoice = NewInvoice();
        var (taxEvent, due) = (invoice.TaxEventDate, invoice.DueDate);
        var date = TestData.Date(2025, 2026);

        InvoiceChanges.Apply(invoice, NoChanges with { InvoiceDate = date }, Today).ShouldBeNull();

        invoice.InvoiceDate.ShouldBe(date);
        (invoice.TaxEventDate, invoice.DueDate).ShouldBe((taxEvent, due));
    }

    [Fact]
    public void Apply_Paid_SetsPaidDateToToday()
    {
        var invoice = NewInvoice();

        InvoiceChanges
            .Apply(invoice, NoChanges with { PaymentStatus = PaymentStatus.Paid }, Today)
            .ShouldBeNull();

        invoice.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        invoice.PaidDate.ShouldBe(Today.Date);
    }

    [Fact]
    public void Apply_PaidWithAPaidDate_UsesThatDate()
    {
        var invoice = NewInvoice();
        var paidDate = TestData.Date(2025, 2026);

        InvoiceChanges
            .Apply(
                invoice,
                NoChanges with
                {
                    PaymentStatus = PaymentStatus.Paid,
                    PaidDate = paidDate,
                },
                Today
            )
            .ShouldBeNull();

        invoice.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        invoice.PaidDate.ShouldBe(paidDate);
    }

    [Fact]
    public void Apply_AlreadyPaid_KeepsThePaidDateUnlessOneIsGiven()
    {
        var invoice = PaidInvoice();
        var paidDate = invoice.PaidDate;

        InvoiceChanges
            .Apply(invoice, NoChanges with { PaymentStatus = PaymentStatus.Paid }, Today)
            .ShouldBeNull();
        invoice.PaidDate.ShouldBe(paidDate);

        InvoiceChanges
            .Apply(invoice, NoChanges with { PaymentMethod = PaymentMethod.Cash }, Today)
            .ShouldBeNull();
        invoice.PaidDate.ShouldBe(paidDate);
        invoice.PaymentMethod.ShouldBe(PaymentMethod.Cash);

        var corrected = TestData.Date(2025, 2026);
        InvoiceChanges
            .Apply(invoice, NoChanges with { PaidDate = corrected }, Today)
            .ShouldBeNull();
        invoice.PaidDate.ShouldBe(corrected);
        invoice.PaymentStatus.ShouldBe(PaymentStatus.Paid);
    }

    [Fact]
    public void Apply_Unpaid_ClearsThePaidDate()
    {
        var invoice = PaidInvoice();

        InvoiceChanges
            .Apply(invoice, NoChanges with { PaymentStatus = PaymentStatus.Unpaid }, Today)
            .ShouldBeNull();

        invoice.PaymentStatus.ShouldBe(PaymentStatus.Unpaid);
        invoice.PaidDate.ShouldBeNull();
        invoice.PaymentMethod.ShouldBe(PaymentMethod.Card);
    }

    [Fact]
    public void Apply_PaidDateOnAnUnpaidInvoice_IsRefusedAndChangesNothing()
    {
        var paidDate = TestData.Date(2025, 2026);
        var cases = new (Invoice Invoice, InvoiceUpdateDto Dto)[]
        {
            // Stays Unpaid.
            (NewInvoice(), NoChanges with { PaidDate = paidDate }),
            // Marked Unpaid in the same request.
            (
                PaidInvoice(),
                NoChanges with
                {
                    PaymentStatus = PaymentStatus.Unpaid,
                    PaidDate = paidDate,
                }
            ),
        };

        foreach (var (invoice, dto) in cases)
        {
            var (editable, frozen) = (Editable(invoice), Frozen(invoice));

            InvoiceChanges
                .Apply(
                    invoice,
                    dto with
                    {
                        Number = OrderBuilder.InvoiceNumber(),
                        DueDate = TestData.Date(2026, 2027),
                    },
                    Today
                )
                .ShouldBe("PaidDate can only be set on a Paid invoice.");

            Editable(invoice).ShouldBe(editable);
            Frozen(invoice).ShouldBe(frozen);
        }
    }
}
