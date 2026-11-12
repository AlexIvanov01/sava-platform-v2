using System.Globalization;
using OrderService.Invoicing;
using OrderService.Mappings;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class InvoiceFactoryTests
{
    private static readonly decimal[] Rates = [20m, 9m, 0m, 20m, 9m];

    private static OrderItem ItemWithRate(decimal vatRate)
    {
        var item = OrderBuilder.Item(batchCount: 2);
        item.VatRate = vatRate;
        return item;
    }

    [Fact]
    public void Create_CopiesTheOrderSupplierAndCustomerWithDefaults()
    {
        var order = OrderBuilder.Order(OrderBuilder.Item(), OrderBuilder.Item(batchCount: 2));
        var company = CompanySettingsBuilder.Entity();
        var number = OrderBuilder.InvoiceNumber();
        var now = TestData.Date(2025, 2026);

        var invoice = InvoiceFactory.Create(order, company, number, now);

        invoice.OrderId.ShouldBe(order.Id);
        invoice.Order.ShouldBeSameAs(order);
        invoice.Number.ShouldBe(number);
        invoice.InvoiceDate.ShouldBe(now);
        invoice.TaxEventDate.ShouldBe(now);
        invoice.DueDate.ShouldBe(now.Date.AddDays(company.PaymentTermsDays!.Value));
        invoice.PaymentMethod.ShouldBe(PaymentMethod.BankTransfer);
        invoice.PaymentStatus.ShouldBe(PaymentStatus.Unpaid);
        invoice.PaidDate.ShouldBeNull();
        invoice.VatExemptionReason.ShouldBeNull();
        invoice.Supplier.ToReadDto().ShouldBe(company.ToSupplierDto());
        invoice.Customer.ToReadDto().ShouldBe(order.Customer!.ToReadDto());
        invoice.Customer.ShouldNotBeSameAs(order.Customer);
    }

    [Fact]
    public void Create_NoPaymentTerms_IsDueInFourteenDays()
    {
        var order = OrderBuilder.Order(OrderBuilder.Item());
        var company = CompanySettingsBuilder.Entity();
        company.PaymentTermsDays = null;
        var now = TestData.Date(2025, 2026);

        var invoice = InvoiceFactory.Create(order, company, OrderBuilder.InvoiceNumber(), now);

        InvoiceFactory.DefaultPaymentTermsDays.ShouldBe(14);
        invoice.DueDate.ShouldBe(now.Date.AddDays(14));
    }

    [Fact]
    public void Create_ZeroPaymentTerms_IsDueOnTheInvoiceDay()
    {
        var company = CompanySettingsBuilder.Entity();
        company.PaymentTermsDays = 0;
        var now = TestData.Date(2025, 2026);

        var invoice = InvoiceFactory.Create(
            OrderBuilder.Order(OrderBuilder.Item()),
            company,
            OrderBuilder.InvoiceNumber(),
            now
        );

        invoice.DueDate.ShouldBe(now.Date);
    }

    [Fact]
    public void Create_ItemsMatchTheOrderItemsAndTheirFigures()
    {
        var items = Enumerable.Range(0, 4).Select(_ => OrderBuilder.Item(batchCount: 2)).ToArray();
        // An edited price is invoiced, not the list price.
        items[0].UnitPrice = TestData.Faker.Finance.Amount(1, 100);
        var order = OrderBuilder.Order(items);

        var invoice = OrderBuilder.Invoice(order);

        var expected = order.Items.OrderBy(i => i.Name).ThenBy(i => i.ProductId).ToList();
        invoice.Items.Count.ShouldBe(expected.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            var source = expected[i];
            var item = invoice.Items[i];
            item.ProductId.ShouldBe(source.ProductId);
            item.SKU.ShouldBe(source.SKU);
            item.Name.ShouldBe(source.Name);
            item.UnitOfMeasure.ShouldBe(source.UnitOfMeasure);
            item.Quantity.ShouldBe(source.Quantity);
            item.Quantity.ShouldBe(source.Batches.Sum(b => b.Quantity));
            item.UnitPrice.ShouldBe(source.UnitPrice!.Value);
            item.VatRate.ShouldBe(source.VatRate!.Value);
            item.Net.ShouldBe(source.Net());
            item.Vat.ShouldBe(source.Vat());
            item.Gross.ShouldBe(source.Gross());
        }
    }

    [Fact]
    public void Create_ItemWithoutPriceOrRate_IsInvoicedAtZero()
    {
        var item = OrderBuilder.Item();
        item.UnitPrice = null;
        item.VatRate = null;

        var invoiced = OrderBuilder.Invoice(OrderBuilder.Order(item)).Items.ShouldHaveSingleItem();

        (invoiced.UnitPrice, invoiced.VatRate, invoiced.Net, invoiced.Vat, invoiced.Gross).ShouldBe(
            (0m, 0m, 0m, 0m, 0m)
        );
        invoiced.Quantity.ShouldBe(item.Quantity);
    }

    [Fact]
    public void Create_VatTotalsArePerRateInAscendingOrder()
    {
        var order = OrderBuilder.Order([.. Rates.Select(ItemWithRate)]);

        var invoice = OrderBuilder.Invoice(order);

        invoice.VatTotals.Select(t => t.VatRate).ShouldBe([0m, 9m, 20m]);
        foreach (var total in invoice.VatTotals)
        {
            var items = order.Items.Where(i => i.VatRate == total.VatRate).ToList();
            total.Net.ShouldBe(items.Net());
            total.Vat.ShouldBe(items.Vat());
        }
        invoice.VatTotals.Single(t => t.VatRate == 0m).Vat.ShouldBe(0m);
    }

    [Fact]
    public void Create_TotalsAreTheSumsOfTheItems()
    {
        var order = OrderBuilder.Order([.. Rates.Select(ItemWithRate)]);

        var invoice = OrderBuilder.Invoice(order);

        invoice.Net.ShouldBe(order.Items.Net());
        invoice.Vat.ShouldBe(order.Items.Vat());
        invoice.Gross.ShouldBe(order.Items.Gross());
        invoice.Net.ShouldBe(invoice.Items.Sum(i => i.Net));
        invoice.Vat.ShouldBe(invoice.Items.Sum(i => i.Vat));
        invoice.Gross.ShouldBe(invoice.Items.Sum(i => i.Gross));
        invoice.Net.ShouldBe(invoice.VatTotals.Sum(t => t.Net));
        invoice.Vat.ShouldBe(invoice.VatTotals.Sum(t => t.Vat));
        invoice.Gross.ShouldBe(invoice.Net + invoice.Vat);
    }

    [Fact]
    public void Create_EmptyOrder_HasNoItemsAndZeroTotals()
    {
        var invoice = OrderBuilder.Invoice(OrderBuilder.Order());

        invoice.Items.ShouldBeEmpty();
        invoice.VatTotals.ShouldBeEmpty();
        (invoice.Net, invoice.Vat, invoice.Gross).ShouldBe((0m, 0m, 0m));
    }

    [Fact]
    public void Create_LaterChangesToTheOrderOrSettings_DoNotReachTheInvoice()
    {
        var order = OrderBuilder.Order(OrderBuilder.Item());
        var company = CompanySettingsBuilder.Entity();
        var invoice = OrderBuilder.Invoice(order, company);
        var customer = invoice.Customer.ToReadDto();
        var supplier = invoice.Supplier.ToReadDto();
        var item = invoice.Items.ShouldHaveSingleItem();
        var (name, net, gross) = (item.Name, item.Net, invoice.Gross);

        var changed = CompanySettingsBuilder.Dto();
        var otherCustomer = OrderBuilder.Customer();
        order.Customer!.Name = otherCustomer.Name;
        order.Customer.VATNumber = otherCustomer.VATNumber;
        company.Name = changed.Name;
        company.IBAN = changed.IBAN;
        order.Items[0].UnitPrice += 1m;
        order.Items[0].Name = OrderBuilder.Product().Name;

        invoice.Customer.ToReadDto().ShouldBe(customer);
        invoice.Supplier.ToReadDto().ShouldBe(supplier);
        (item.Name, item.Net, invoice.Gross).ShouldBe((name, net, gross));
    }

    [Fact]
    public void Create_OrderWithoutCustomer_GetsAnEmptyCustomer()
    {
        var order = OrderBuilder.Order(OrderBuilder.Item());
        order.Customer = null;

        var invoice = OrderBuilder.Invoice(order);

        invoice.Customer.ShouldNotBeNull().ToReadDto().ShouldBe(new CustomerSnapshot().ToReadDto());
    }

    [Fact]
    public void CustomerSnapshotCopy_IsEqualButSeparate()
    {
        var customer = OrderBuilder.Customer();

        var copy = customer.Copy();

        copy.ShouldNotBeSameAs(customer);
        copy.ToReadDto().ShouldBe(customer.ToReadDto());
        copy.Name = OrderBuilder.Customer().Name;
        customer.ToReadDto().ShouldNotBe(copy.ToReadDto());
    }

    [Theory]
    [InlineData(null, "0000000001")]
    [InlineData("", "0000000001")]
    [InlineData("0000000001", "0000000002")]
    [InlineData("0000000009", "0000000010")]
    [InlineData("0000000041", "0000000042")]
    [InlineData("0999999999", "1000000000")]
    [InlineData("not-number", "0000000001")]
    public void NextNumber_IsOneMoreThanTheHighestWithTenDigits(string? highest, string next)
    {
        InvoiceFactory.NextNumber(highest).ShouldBe(next);
    }

    [Fact]
    public void NextNumber_OfARandomNumber_IsItsSuccessor()
    {
        var value = TestData.Faker.Random.Long(0, 999_999_998);
        var highest = value.ToString("D10", CultureInfo.InvariantCulture);

        var next = InvoiceFactory.NextNumber(highest);

        next.Length.ShouldBe(10);
        long.Parse(next, CultureInfo.InvariantCulture).ShouldBe(value + 1);
    }
}

public class InvoiceOverdueTests
{
    private DateOnly Today { get; } = DateOnly.FromDateTime(TestData.Date(2025, 2026));

    private static Invoice Invoice(PaymentStatus status, DateTime? due) =>
        new() { PaymentStatus = status, DueDate = due };

    private static DateTime At(DateOnly day, int hour, int minute = 0) =>
        day.ToDateTime(new TimeOnly(hour, minute));

    [Fact]
    public void UnpaidAndDueBeforeToday_IsOverdue()
    {
        Invoice(PaymentStatus.Unpaid, At(Today.AddDays(-1), 23, 59))
            .IsOverdue(Today)
            .ShouldBeTrue();
        Invoice(PaymentStatus.Unpaid, At(Today.AddDays(-TestData.Faker.Random.Int(2, 400)), 0))
            .IsOverdue(Today)
            .ShouldBeTrue();
    }

    [Fact]
    public void UnpaidAndDueTodayOrLater_IsNotOverdue()
    {
        Invoice(PaymentStatus.Unpaid, At(Today, 0)).IsOverdue(Today).ShouldBeFalse();
        Invoice(PaymentStatus.Unpaid, At(Today, 23, 59)).IsOverdue(Today).ShouldBeFalse();
        Invoice(PaymentStatus.Unpaid, At(Today.AddDays(1), 0)).IsOverdue(Today).ShouldBeFalse();
    }

    [Fact]
    public void Paid_IsNeverOverdue()
    {
        Invoice(PaymentStatus.Paid, At(Today.AddDays(-30), 12)).IsOverdue(Today).ShouldBeFalse();
    }

    [Fact]
    public void NoDueDate_IsNotOverdue()
    {
        Invoice(PaymentStatus.Unpaid, null).IsOverdue(Today).ShouldBeFalse();
    }
}
