using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using OrderService.Dtos;
using OrderService.Invoicing;
using OrderService.Mappings;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

// Invoices through the real app. The Development environment seeds the company settings (the
// orders aren't seeded, since the substituted services don't know the seeded customers).
[Trait("Category", "Integration")]
public class InvoiceApiTests(OrderApiFactory factory) : IClassFixture<OrderApiFactory>
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

    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private OrderItemWriteDto ItemDto(int product, int quantity) =>
        OrderBuilder.ItemDto(
            factory.Products[product].Id,
            (factory.Batches[2 * product].Id, quantity),
            (factory.Batches[2 * product + 1].Id, quantity + 1)
        );

    // A completed order (both products) and its invoice.
    private async Task<(OrderReadDto Order, InvoiceReadDto Invoice)> CompleteOrderAsync()
    {
        var f = TestData.Faker;
        var created = await _client.PostAsJsonAsync(
            "/api/orders",
            OrderBuilder.CreateDto(
                factory.CustomerId,
                ItemDto(0, f.Random.Int(1, 10)),
                ItemDto(1, f.Random.Int(1, 10))
            ),
            Ct
        );
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<OrderReadDto>(Ct)).ShouldNotBeNull().Id;
        var completed = await _client.PostAsJsonAsync(
            $"/api/orders/{id}/status",
            OrderBuilder.StatusDto(OrderStatus.Completed),
            Ct
        );
        completed.StatusCode.ShouldBe(HttpStatusCode.OK);
        // Read back, so dates have the database's precision like the invoice's.
        var order = (
            await _client.GetFromJsonAsync<OrderReadDto>($"/api/orders/{id}", Ct)
        ).ShouldNotBeNull();
        return (order, await GetInvoiceAsync(order.Invoice.ShouldNotBeNull().Id));
    }

    private async Task<InvoiceReadDto> GetInvoiceAsync(Guid id) =>
        (
            await _client.GetFromJsonAsync<InvoiceReadDto>($"/api/invoices/{id}", Ct)
        ).ShouldNotBeNull();

    private Task<HttpResponseMessage> PutAsync(Guid id, InvoiceUpdateDto dto) =>
        _client.PutAsJsonAsync($"/api/invoices/{id}", dto, Ct);

    private async Task<InvoiceReadDto> PutOkAsync(Guid id, InvoiceUpdateDto dto)
    {
        var response = await PutAsync(id, dto);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<InvoiceReadDto>(Ct)).ShouldNotBeNull();
    }

    private async Task<CompanySettingsReadDto> GetSettingsAsync() =>
        (
            await _client.GetFromJsonAsync<CompanySettingsReadDto>("/api/companysettings", Ct)
        ).ShouldNotBeNull();

    // The invoices the list returns for a query (none when it answers 404).
    private async Task<List<InvoiceReadDto>> ListAsync(string query)
    {
        var response = await _client.GetAsync($"/api/invoices?pageSize=100&{query}", Ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var page = (
            await response.Content.ReadFromJsonAsync<InvoicePageReadDto>(Ct)
        ).ShouldNotBeNull();
        return [.. page.Invoices];
    }

    [Fact]
    public async Task Complete_CreatesTheFullInvoiceWithTheSupplierFromTheSettings()
    {
        var settings = await GetSettingsAsync();

        var (order, invoice) = await CompleteOrderAsync();

        invoice.OrderId.ShouldBe(order.Id);
        invoice.OrderStatus.ShouldBe(OrderStatus.Completed);
        invoice.Number.ShouldBe(order.Invoice!.Number);
        invoice.InvoiceDate.ShouldBe(order.CompletedDate);
        invoice.TaxEventDate.ShouldBe(order.CompletedDate);
        invoice.DueDate.ShouldBe(
            order.CompletedDate!.Value.Date.AddDays(
                settings.PaymentTermsDays ?? InvoiceFactory.DefaultPaymentTermsDays
            )
        );
        (invoice.PaymentMethod, invoice.PaymentStatus, invoice.PaidDate).ShouldBe(
            (PaymentMethod.BankTransfer, PaymentStatus.Unpaid, (DateTime?)null)
        );
        invoice.IsOverdue.ShouldBeFalse();
        invoice.VatExemptionReason.ShouldBeNull();
        invoice.Supplier.ShouldBe(settings.ToSupplierDto());
        invoice.Customer.ShouldBe(factory.Customer.ToReadDto());
        invoice
            .Items.Select(i =>
                (
                    i.ProductId,
                    i.SKU,
                    i.Name,
                    i.Quantity,
                    i.UnitPrice,
                    i.VatRate,
                    i.Net,
                    i.Vat,
                    i.Gross
                )
            )
            .ShouldBe(
                order.Items.Select(i =>
                    (
                        i.ProductId,
                        i.SKU,
                        i.Name,
                        i.Quantity,
                        i.UnitPrice!.Value,
                        i.VatRate!.Value,
                        i.Net,
                        i.Vat,
                        i.Gross
                    )
                )
            );
        invoice.VatTotals.ShouldBe([
            .. order
                .Items.GroupBy(i => i.VatRate!.Value)
                .OrderBy(g => g.Key)
                .Select(g => new InvoiceVatTotalReadDto(
                    g.Key,
                    g.Sum(i => i.Net),
                    g.Sum(i => i.Vat)
                )),
        ]);
        (invoice.Net, invoice.Vat, invoice.Gross).ShouldBe(
            (order.Totals.Net, order.Totals.Vat, order.Totals.Gross)
        );
        invoice.Audit.CreatedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task UpdatePayment_PaidSetsThePaidDateAndUnpaidClearsIt()
    {
        var (_, invoice) = await CompleteOrderAsync();
        var paidDate = TestData.Date(2026, 2026);

        var paid = await PutOkAsync(
            invoice.Id,
            NoChanges with
            {
                PaymentStatus = PaymentStatus.Paid,
            }
        );
        paid.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        paid.PaidDate.ShouldNotBeNull()
            .ShouldBeGreaterThanOrEqualTo(invoice.InvoiceDate!.Value.Date);
        paid.Audit.UpdatedAt.ShouldNotBeNull();

        var corrected = await PutOkAsync(
            invoice.Id,
            NoChanges with
            {
                PaidDate = paidDate,
                PaymentMethod = PaymentMethod.Cash,
            }
        );
        (corrected.PaidDate, corrected.PaymentMethod).ShouldBe((paidDate, PaymentMethod.Cash));

        var unpaid = await PutOkAsync(
            invoice.Id,
            NoChanges with
            {
                PaymentStatus = PaymentStatus.Unpaid,
            }
        );
        unpaid.PaidDate.ShouldBeNull();
        var saved = await GetInvoiceAsync(invoice.Id);
        (saved.PaymentStatus, saved.PaidDate, saved.PaymentMethod).ShouldBe(
            (PaymentStatus.Unpaid, (DateTime?)null, PaymentMethod.Cash)
        );
        // Nothing else changed.
        (saved.Number, saved.InvoiceDate, saved.DueDate, saved.Gross).ShouldBe(
            (invoice.Number, invoice.InvoiceDate, invoice.DueDate, invoice.Gross)
        );
        saved.Supplier.ShouldBe(invoice.Supplier);
        saved.Items.ShouldBe(invoice.Items);
    }

    [Fact]
    public async Task UpdateInvoice_InvalidChanges_Return400AndChangeNothing()
    {
        var (_, invoice) = await CompleteOrderAsync();

        var problem = await PutAsync(
            invoice.Id,
            NoChanges with
            {
                Number = OrderBuilder.InvoiceNumber(),
                PaidDate = TestData.Date(2026, 2026),
            }
        );
        problem.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var details = (
            await problem.Content.ReadFromJsonAsync<ProblemDetails>(Ct)
        ).ShouldNotBeNull();
        details.Title.ShouldBe("Invalid invoice update");
        details.Detail.ShouldBe("PaidDate can only be set on a Paid invoice.");

        foreach (var number in new[] { "123", "12345678901", "abcdefghij" })
        {
            (await PutAsync(invoice.Id, NoChanges with { Number = number })).StatusCode.ShouldBe(
                HttpStatusCode.BadRequest,
                number
            );
        }
        (
            await PutAsync(invoice.Id, NoChanges with { VatExemptionReason = new string('x', 501) })
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var saved = await GetInvoiceAsync(invoice.Id);
        (saved.Number, saved.PaidDate, saved.VatExemptionReason).ShouldBe(
            (invoice.Number, (DateTime?)null, (string?)null)
        );
    }

    [Fact]
    public async Task Numbering_ReportsTakenNumbersAndTheNextOne()
    {
        var (_, invoice) = await CompleteOrderAsync();
        // Above every number the other tests use, so it is the highest.
        var number = "9" + TestData.Faker.Random.ReplaceNumbers("########") + "0";
        await PutOkAsync(invoice.Id, NoChanges with { Number = number });

        var taken = await _client.GetFromJsonAsync<InvoiceNumberCheckDto>(
            $"/api/invoices/numbering?number={number}",
            Ct
        );
        taken.ShouldBe(new InvoiceNumberCheckDto(number, true, InvoiceFactory.NextNumber(number)));
        var free = InvoiceFactory.NextNumber(number);
        (
            await _client.GetFromJsonAsync<InvoiceNumberCheckDto>(
                $"/api/invoices/numbering?number={free}",
                Ct
            )
        ).ShouldBe(new InvoiceNumberCheckDto(free, false, free));

        // The next completed order gets that number.
        var (_, next) = await CompleteOrderAsync();
        next.Number.ShouldBe(free);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?number=")]
    [InlineData("?number=123")]
    [InlineData("?number=12345678901")]
    [InlineData("?number=12345abcde")]
    public async Task Numbering_WithoutATenDigitNumber_Returns400(string query)
    {
        (await _client.GetAsync($"/api/invoices/numbering{query}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.BadRequest
        );
    }

    [Fact]
    public async Task List_FiltersByNumberPaymentStatusOverdueCustomerAndDate()
    {
        var (order, overdue) = await CompleteOrderAsync();
        var (_, paid) = await CompleteOrderAsync();
        // Only this test uses numbers starting with 5.
        var number = "5" + TestData.Faker.Random.ReplaceNumbers("#########");
        overdue = await PutOkAsync(
            overdue.Id,
            NoChanges with
            {
                Number = number,
                DueDate = TestData.Date(2020, 2024),
            }
        );
        paid = await PutOkAsync(paid.Id, NoChanges with { PaymentStatus = PaymentStatus.Paid });
        overdue.IsOverdue.ShouldBeTrue();
        paid.IsOverdue.ShouldBeFalse();
        var day = DateOnly.FromDateTime(order.CompletedDate!.Value);

        (await ListAsync($"number={number}")).ShouldHaveSingleItem().Id.ShouldBe(overdue.Id);
        (await ListAsync($"number={number[..6]}")).Select(i => i.Id).ShouldContain(overdue.Id);

        var paidList = await ListAsync("paymentStatus=Paid");
        paidList.Select(i => i.Id).ShouldContain(paid.Id);
        paidList.Select(i => i.Id).ShouldNotContain(overdue.Id);
        paidList.ShouldAllBe(i => i.PaymentStatus == PaymentStatus.Paid);
        var unpaidList = await ListAsync("paymentStatus=Unpaid");
        unpaidList.Select(i => i.Id).ShouldContain(overdue.Id);
        unpaidList.Select(i => i.Id).ShouldNotContain(paid.Id);

        var overdueList = await ListAsync("overdue=true");
        overdueList.Select(i => i.Id).ShouldContain(overdue.Id);
        overdueList.ShouldAllBe(i => i.IsOverdue);
        var notOverdue = await ListAsync("overdue=false");
        notOverdue.Select(i => i.Id).ShouldContain(paid.Id);
        notOverdue.ShouldAllBe(i => !i.IsOverdue);

        (await ListAsync($"overdue=true&number={number}")).ShouldHaveSingleItem();
        (await ListAsync($"overdue=false&number={number}")).ShouldBeEmpty();
        (await ListAsync($"paymentStatus=Paid&number={number}")).ShouldBeEmpty();

        (await ListAsync($"customerId={factory.CustomerId}"))
            .Select(i => i.Id)
            .ShouldContain(overdue.Id);
        (await ListAsync($"customerId={TestData.Faker.Random.Guid()}")).ShouldBeEmpty();

        (await ListAsync($"from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}"))
            .Select(i => i.Id)
            .ShouldContain(overdue.Id);
        (await ListAsync($"from={day.AddDays(1):yyyy-MM-dd}&number={number}")).ShouldBeEmpty();
        (await ListAsync($"to={day.AddDays(-1):yyyy-MM-dd}&number={number}")).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task List_PageSizeOutOfRange_Returns400(int pageSize)
    {
        (await _client.GetAsync($"/api/invoices?pageSize={pageSize}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.BadRequest
        );
    }

    [Fact]
    public async Task UnknownOrNonGuidIds_Return404()
    {
        var unknown = TestData.Faker.Random.Guid();

        foreach (var id in new[] { "abc", "123", unknown.ToString() })
        {
            (await _client.GetAsync($"/api/invoices/{id}", Ct)).StatusCode.ShouldBe(
                HttpStatusCode.NotFound,
                id
            );
            (await _client.DeleteAsync($"/api/invoices/{id}", Ct)).StatusCode.ShouldBe(
                HttpStatusCode.NotFound,
                id
            );
        }
        (await PutAsync(unknown, NoChanges)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ChangingTheCompanySettings_DoesNotChangeExistingInvoices()
    {
        var before = await GetSettingsAsync();
        var (_, invoice) = await CompleteOrderAsync();
        var changed = CompanySettingsBuilder.Dto();

        (await _client.PutAsJsonAsync("/api/companysettings", changed, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.OK
        );

        var existing = await GetInvoiceAsync(invoice.Id);
        existing.Supplier.ShouldBe(before.ToSupplierDto());
        existing.DueDate.ShouldBe(invoice.DueDate);
        // Invoices created afterwards use the new settings.
        var (order, next) = await CompleteOrderAsync();
        next.Supplier.ShouldBe(changed.ToEntity().ToSupplierDto());
        next.DueDate.ShouldBe(
            order.CompletedDate!.Value.Date.AddDays(changed.PaymentTermsDays!.Value)
        );
    }
}
