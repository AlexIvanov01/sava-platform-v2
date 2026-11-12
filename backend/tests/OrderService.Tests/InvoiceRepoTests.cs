using Microsoft.EntityFrameworkCore;
using OrderService.Auditing;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.Invoicing;
using OrderService.Mappings;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class InvoiceRepoTests(MySqlFixture mySql) : OrderDbTestBase(mySql)
{
    private static readonly InvoiceFilter NoFilter = new(null, null, null, null, null, null);

    private DateOnly Today { get; } = DateOnly.FromDateTime(TestData.Date(2026, 2027));

    // An invoiced order as completing it creates one; configure adjusts the invoice before saving.
    private async Task<(Order Order, Invoice Invoice)> SeedInvoicedOrderAsync(
        OrderStatus status = OrderStatus.Completed,
        Guid? customerId = null,
        Action<Invoice>? configure = null
    )
    {
        var order = OrderBuilder.OrderFor(
            customerId ?? TestData.Faker.Random.Guid(),
            OrderBuilder.Item(),
            OrderBuilder.Item(batchCount: 2)
        );
        order.Status = status;
        order.CompletedDate = TestData.Date(2025, 2026);
        if (status == OrderStatus.Cancelled)
        {
            order.CancelledDate = TestData.Date(2025, 2026);
        }
        var invoice = OrderBuilder.Invoice(order, date: order.CompletedDate);
        configure?.Invoke(invoice);
        await SeedAsync(order);
        return (order, invoice);
    }

    private Task<Invoice> SeedInvoiceAsync(Action<Invoice> configure) =>
        SeedInvoiceAsync(null, configure);

    private async Task<Invoice> SeedInvoiceAsync(Guid? customerId, Action<Invoice> configure) =>
        (await SeedInvoicedOrderAsync(customerId: customerId, configure: configure)).Invoice;

    private async Task<List<Invoice>> GetAsync(
        InvoiceFilter filter,
        Guid? cursor = null,
        int pageSize = 100
    )
    {
        await using var context = Db.CreateContext();
        return
        [
            .. await new InvoiceRepo(context).GetInvoicesAsync(filter, Today, cursor, pageSize),
        ];
    }

    private async Task<int> CountAsync(InvoiceFilter filter)
    {
        await using var context = Db.CreateContext();
        return await new InvoiceRepo(context).GetInvoiceCountAsync(filter, Today);
    }

    private async Task<List<T>> QueryAsync<T>(FormattableString sql)
    {
        await using var context = Db.CreateContext();
        return await context.Database.SqlQuery<T>(sql).ToListAsync(Ct);
    }

    [Fact]
    public async Task SavedInvoice_RoundTripsEverySnapshotField()
    {
        var (order, invoice) = await SeedInvoicedOrderAsync(configure: i =>
        {
            i.PaymentMethod = PaymentMethod.Cash;
            i.VatExemptionReason = TestData.Faker.Lorem.Sentence();
        });
        var expected = invoice.ToReadDto(Today);

        await using var context = Db.CreateContext();
        var loaded = (
            await new InvoiceRepo(context).GetInvoiceByIdAsync(invoice.Id)
        ).ShouldNotBeNull();

        loaded.Order.ShouldNotBeNull().Id.ShouldBe(order.Id);
        var dto = loaded.ToReadDto(Today);
        (dto.Id, dto.OrderId, dto.OrderStatus, dto.Number).ShouldBe(
            (invoice.Id, order.Id, OrderStatus.Completed, invoice.Number)
        );
        (dto.InvoiceDate, dto.TaxEventDate, dto.DueDate, dto.PaidDate).ShouldBe(
            (expected.InvoiceDate, expected.TaxEventDate, expected.DueDate, (DateTime?)null)
        );
        (dto.PaymentMethod, dto.PaymentStatus, dto.VatExemptionReason).ShouldBe(
            (PaymentMethod.Cash, PaymentStatus.Unpaid, expected.VatExemptionReason)
        );
        dto.Supplier.ShouldBe(expected.Supplier);
        dto.Customer.ShouldBe(expected.Customer);
        dto.Customer.ShouldBe(order.Customer!.ToReadDto());
        dto.Items.Count.ShouldBe(2);
        dto.Items.ShouldBe(expected.Items, ignoreOrder: true);
        dto.VatTotals.ShouldBe(expected.VatTotals);
        (dto.Net, dto.Vat, dto.Gross).ShouldBe((expected.Net, expected.Vat, expected.Gross));
        dto.Audit.CreatedAt.ShouldNotBeNull();
        dto.Audit.CreatedBy.ShouldBe(HttpContextCurrentUser.SystemName);
    }

    [Fact]
    public async Task SavedInvoice_StoresEnumsAsNames()
    {
        await SeedInvoiceAsync(i =>
        {
            i.PaymentMethod = PaymentMethod.Card;
            i.PaymentStatus = PaymentStatus.Paid;
            i.PaidDate = TestData.Date(2026, 2026);
        });

        (await QueryAsync<string>($"SELECT PaymentMethod AS Value FROM Invoices")).ShouldBe([
            nameof(PaymentMethod.Card),
        ]);
        (await QueryAsync<string>($"SELECT PaymentStatus AS Value FROM Invoices")).ShouldBe([
            nameof(PaymentStatus.Paid),
        ]);
    }

    [Fact]
    public async Task SecondInvoiceForAnOrder_IsRejectedByTheDatabase()
    {
        var (order, _) = await SeedInvoicedOrderAsync();

        await using var context = Db.CreateContext();
        var second = InvoiceFactory.Create(
            order,
            CompanySettingsBuilder.Entity(),
            OrderBuilder.InvoiceNumber(),
            TestData.Date(2026, 2026)
        );
        second.Order = null;
        context.Invoices.Add(second);
        await Should.ThrowAsync<DbUpdateException>(context.SaveChangesAsync(Ct));
    }

    [Fact]
    public async Task DuplicateNumbers_AreAllowed()
    {
        var number = OrderBuilder.InvoiceNumber();

        await SeedInvoiceAsync(i => i.Number = number);
        await SeedInvoiceAsync(i => i.Number = number);

        (await QueryAsync<string>($"SELECT Number AS Value FROM Invoices")).ShouldBe([
            number,
            number,
        ]);
        await using var context = Db.CreateContext();
        var repo = new InvoiceRepo(context);
        (await repo.NumberExistsAsync(number)).ShouldBeTrue();
        (await repo.NextNumberAsync()).ShouldBe(InvoiceFactory.NextNumber(number));
        (await repo.GetInvoiceCountAsync(NoFilter with { Number = number }, Today)).ShouldBe(2);
    }

    [Fact]
    public async Task GetInvoiceByIdAsync_UnknownId_ReturnsNull()
    {
        await SeedInvoicedOrderAsync();

        await using var context = Db.CreateContext();
        (
            await new InvoiceRepo(context).GetInvoiceByIdAsync(TestData.Faker.Random.Guid())
        ).ShouldBeNull();
    }

    [Fact]
    public async Task GetInvoiceForUpdateAsync_TracksTheInvoiceSoChangesAreSaved()
    {
        var (order, invoice) = await SeedInvoicedOrderAsync();
        var dto = new InvoiceUpdateDto(
            OrderBuilder.InvoiceNumber(),
            null,
            null,
            TestData.Date(2026, 2027),
            PaymentMethod.Card,
            PaymentStatus.Paid,
            null,
            null
        );
        var now = TestData.Date(2026, 2027);

        await using (var context = Db.CreateContext())
        {
            var repo = new InvoiceRepo(context);
            (await repo.GetInvoiceForUpdateAsync(TestData.Faker.Random.Guid())).ShouldBeNull();
            var tracked = (await repo.GetInvoiceForUpdateAsync(invoice.Id)).ShouldNotBeNull();
            tracked.Order.ShouldNotBeNull().Status.ShouldBe(OrderStatus.Completed);
            InvoiceChanges.Apply(tracked, dto, now).ShouldBeNull();
            await repo.SaveChangesAsync();
        }

        await using var assert = Db.CreateContext();
        var saved = await assert.Invoices.SingleAsync(Ct);
        (saved.Number, saved.DueDate, saved.PaymentMethod, saved.PaymentStatus).ShouldBe(
            (dto.Number, dto.DueDate, PaymentMethod.Card, PaymentStatus.Paid)
        );
        saved.PaidDate.ShouldBe(now.Date);
        saved.InvoiceDate.ShouldBe(invoice.InvoiceDate);
        saved.Gross.ShouldBe(invoice.Gross);
        saved.Items.Count.ShouldBe(invoice.Items.Count);
        saved.UpdatedAt.ShouldNotBeNull();
        (await assert.Orders.SingleAsync(Ct)).Id.ShouldBe(order.Id);
    }

    [Fact]
    public async Task GetInvoicesAsync_PagesByIdCursorWithTheirOrders()
    {
        for (var i = 0; i < 5; i++)
        {
            await SeedInvoicedOrderAsync();
        }

        var all = await GetAsync(NoFilter);
        var first = await GetAsync(NoFilter, null, 2);
        var second = await GetAsync(NoFilter, first[^1].Id, 2);
        var last = await GetAsync(NoFilter, second[^1].Id, 2);

        all.Count.ShouldBe(5);
        first.Select(i => i.Id).ShouldBe(all.Take(2).Select(i => i.Id));
        second.Select(i => i.Id).ShouldBe(all.Skip(2).Take(2).Select(i => i.Id));
        last.ShouldHaveSingleItem().Id.ShouldBe(all[4].Id);
        all.ShouldAllBe(i => i.Order != null && i.Order.Id == i.OrderId);
        all.ShouldAllBe(i => i.Items.Count == 2 && i.Supplier.Name != null);
        (await CountAsync(NoFilter)).ShouldBe(5);
    }

    // Five invoices around the From/To days and today, so every filter keeps a different subset.
    private sealed record FilterData(
        DateOnly From,
        DateOnly To,
        Guid Customer1,
        Guid Customer2,
        Invoice AtStartOfFrom,
        Invoice AtEndOfTo,
        Invoice BeforeFrom,
        Invoice AfterTo,
        Invoice Inside
    );

    private static DateTime At(DateOnly day, int hour, int minute = 0, int second = 0) =>
        day.ToDateTime(new TimeOnly(hour, minute, second));

    private async Task<FilterData> SeedFilterDataAsync()
    {
        var from = DateOnly.FromDateTime(TestData.Date(2025, 2025));
        var to = from.AddDays(TestData.Faker.Random.Int(0, 60));
        var (customer1, customer2) = (TestData.Faker.Random.Guid(), TestData.Faker.Random.Guid());

        // Unpaid and due yesterday: overdue.
        var atStartOfFrom = await SeedInvoiceAsync(
            customer1,
            i =>
            {
                i.InvoiceDate = At(from, 0);
                i.Number = "0000000101";
                i.DueDate = At(Today.AddDays(-1), 23, 59, 59);
            }
        );
        // Paid after its due date: not overdue.
        var atEndOfTo = await SeedInvoiceAsync(
            customer2,
            i =>
            {
                i.InvoiceDate = At(to, 23, 59, 59);
                i.Number = "0000000102";
                i.DueDate = At(Today.AddDays(-30), 12);
                i.PaymentStatus = PaymentStatus.Paid;
                i.PaidDate = At(Today.AddDays(-20), 12);
            }
        );
        // Unpaid and due today: not overdue yet.
        var beforeFrom = await SeedInvoiceAsync(
            customer1,
            i =>
            {
                i.InvoiceDate = At(from.AddDays(-1), 23, 59, 59);
                i.Number = "0000000250";
                i.DueDate = At(Today, 0);
            }
        );
        // Unpaid without a due date: never overdue.
        var afterTo = await SeedInvoiceAsync(
            customer2,
            i =>
            {
                i.InvoiceDate = At(to.AddDays(1), 0);
                i.Number = "0000000351";
                i.DueDate = null;
            }
        );
        // Unpaid and due tomorrow.
        var inside = await SeedInvoiceAsync(i =>
        {
            i.InvoiceDate = At(from, 12);
            i.Number = "0000000999";
            i.DueDate = At(Today.AddDays(1), 0);
        });
        return new(
            from,
            to,
            customer1,
            customer2,
            atStartOfFrom,
            atEndOfTo,
            beforeFrom,
            afterTo,
            inside
        );
    }

    // The filter finds exactly these invoices, and counts them.
    private async Task ShouldFindAsync(InvoiceFilter filter, params Invoice[] expected)
    {
        (await GetAsync(filter))
            .Select(i => i.Id)
            .ShouldBe(expected.Select(i => i.Id), ignoreOrder: true, customMessage: $"{filter}");
        (await CountAsync(filter)).ShouldBe(expected.Length, $"{filter}");
    }

    [Fact]
    public async Task GetInvoicesAsync_FromAndTo_IncludeWholeDaysOfTheInvoiceDate()
    {
        var d = await SeedFilterDataAsync();

        await ShouldFindAsync(
            NoFilter with
            {
                From = d.From,
            },
            d.AtStartOfFrom,
            d.AtEndOfTo,
            d.AfterTo,
            d.Inside
        );
        await ShouldFindAsync(
            NoFilter with
            {
                To = d.To,
            },
            d.AtStartOfFrom,
            d.AtEndOfTo,
            d.BeforeFrom,
            d.Inside
        );
        await ShouldFindAsync(
            NoFilter with
            {
                From = d.From,
                To = d.To,
            },
            d.AtStartOfFrom,
            d.AtEndOfTo,
            d.Inside
        );
        await ShouldFindAsync(NoFilter with { From = d.To.AddDays(2) });
    }

    [Fact]
    public async Task GetInvoicesAsync_CustomerId_MatchesTheOrdersCustomer()
    {
        var d = await SeedFilterDataAsync();

        await ShouldFindAsync(
            NoFilter with
            {
                CustomerId = d.Customer1,
            },
            d.AtStartOfFrom,
            d.BeforeFrom
        );
        await ShouldFindAsync(NoFilter with { CustomerId = d.Customer2 }, d.AtEndOfTo, d.AfterTo);
        await ShouldFindAsync(NoFilter with { CustomerId = TestData.Faker.Random.Guid() });
    }

    [Fact]
    public async Task GetInvoicesAsync_Number_MatchesASubstring()
    {
        var d = await SeedFilterDataAsync();

        await ShouldFindAsync(NoFilter with { Number = "10" }, d.AtStartOfFrom, d.AtEndOfTo);
        await ShouldFindAsync(NoFilter with { Number = "0000000351" }, d.AfterTo);
        await ShouldFindAsync(NoFilter with { Number = "999" }, d.Inside);
        await ShouldFindAsync(NoFilter with { Number = "777" });
        // An empty number doesn't filter.
        (await CountAsync(NoFilter with { Number = "" })).ShouldBe(5);
    }

    [Fact]
    public async Task GetInvoicesAsync_PaymentStatus()
    {
        var d = await SeedFilterDataAsync();

        await ShouldFindAsync(NoFilter with { PaymentStatus = PaymentStatus.Paid }, d.AtEndOfTo);
        await ShouldFindAsync(
            NoFilter with
            {
                PaymentStatus = PaymentStatus.Unpaid,
            },
            d.AtStartOfFrom,
            d.BeforeFrom,
            d.AfterTo,
            d.Inside
        );
    }

    [Fact]
    public async Task GetInvoicesAsync_Overdue_ComparesTheDueDateWithToday()
    {
        var d = await SeedFilterDataAsync();

        await ShouldFindAsync(NoFilter with { Overdue = true }, d.AtStartOfFrom);
        await ShouldFindAsync(
            NoFilter with
            {
                Overdue = false,
            },
            d.AtEndOfTo,
            d.BeforeFrom,
            d.AfterTo,
            d.Inside
        );
        // The filter agrees with Invoice.IsOverdue.
        var overdue = (await GetAsync(NoFilter)).Where(i => i.IsOverdue(Today)).ToList();
        overdue.ShouldHaveSingleItem().Id.ShouldBe(d.AtStartOfFrom.Id);
    }

    [Fact]
    public async Task GetInvoicesAsync_FiltersCombineAndApplyToTheCursor()
    {
        var d = await SeedFilterDataAsync();

        await ShouldFindAsync(
            new InvoiceFilter(d.From, d.To, null, null, PaymentStatus.Unpaid, false),
            d.Inside
        );
        await ShouldFindAsync(
            new InvoiceFilter(null, null, d.Customer1, "0000000", PaymentStatus.Unpaid, null),
            d.AtStartOfFrom,
            d.BeforeFrom
        );

        var unpaid = NoFilter with { PaymentStatus = PaymentStatus.Unpaid };
        var all = await GetAsync(unpaid);
        var first = await GetAsync(unpaid, null, 3);
        var rest = await GetAsync(unpaid, first[^1].Id, 3);
        first.Concat(rest).Select(i => i.Id).ShouldBe(all.Select(i => i.Id));
        rest.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task NextNumberAsync_NoInvoices_StartsAtOne()
    {
        await using var context = Db.CreateContext();
        var repo = new InvoiceRepo(context);

        (await repo.NextNumberAsync()).ShouldBe("0000000001");
        (await repo.NumberExistsAsync("0000000001")).ShouldBeFalse();
    }

    [Fact]
    public async Task NextNumberAsync_IsOneMoreThanTheHighest()
    {
        foreach (var number in new[] { "0000000007", "0000000041", "0000000003" })
        {
            await SeedInvoiceAsync(i => i.Number = number);
        }

        await using var context = Db.CreateContext();
        var repo = new InvoiceRepo(context);
        (await repo.NextNumberAsync()).ShouldBe("0000000042");
        (await repo.NumberExistsAsync("0000000041")).ShouldBeTrue();
        (await repo.NumberExistsAsync("0000000003")).ShouldBeTrue();
        (await repo.NumberExistsAsync("0000000042")).ShouldBeFalse();
        (await repo.NumberExistsAsync("000000004")).ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteInvoiceAsync_CompletedOrder_RemovesInvoiceAndMovesOrderToPending()
    {
        var (order, invoice) = await SeedInvoicedOrderAsync();
        var (other, otherInvoice) = await SeedInvoicedOrderAsync();

        await using (var context = Db.CreateContext())
        {
            var repo = new InvoiceRepo(context);
            (await repo.DeleteInvoiceAsync(invoice.Id)).ShouldNotBeNull().Id.ShouldBe(invoice.Id);
            (await repo.DeleteInvoiceAsync(invoice.Id)).ShouldBeNull();
        }

        await using var assert = Db.CreateContext();
        (await assert.Invoices.SingleAsync(Ct)).Id.ShouldBe(otherInvoice.Id);
        // Its items and VAT totals went with it.
        (await QueryAsync<long>($"SELECT COUNT(*) AS Value FROM InvoiceItems"))
            .ShouldHaveSingleItem()
            .ShouldBe(otherInvoice.Items.Count);
        (await QueryAsync<long>($"SELECT COUNT(*) AS Value FROM InvoiceVatTotals"))
            .ShouldHaveSingleItem()
            .ShouldBe(otherInvoice.VatTotals.Count);
        var saved = await assert
            .Orders.Include(o => o.Items)
            .SingleAsync(o => o.Id == order.Id, Ct);
        saved.Status.ShouldBe(OrderStatus.Pending);
        saved.CompletedDate.ShouldBeNull();
        saved.CancelledDate.ShouldBeNull();
        saved.UpdatedAt.ShouldNotBeNull();
        saved
            .Items.Select(i => i.ProductId)
            .ShouldBe(order.Items.Select(i => i.ProductId), ignoreOrder: true);
        // Other orders are untouched.
        var untouched = await assert.Orders.SingleAsync(o => o.Id == other.Id, Ct);
        untouched.Status.ShouldBe(OrderStatus.Completed);
        untouched.CompletedDate.ShouldBe(other.CompletedDate);
    }

    [Fact]
    public async Task DeleteInvoiceAsync_CancelledOrder_StaysCancelled()
    {
        var (order, invoice) = await SeedInvoicedOrderAsync(OrderStatus.Cancelled);

        await using (var context = Db.CreateContext())
        {
            (await new InvoiceRepo(context).DeleteInvoiceAsync(invoice.Id)).ShouldNotBeNull();
        }

        await using var assert = Db.CreateContext();
        (await assert.Invoices.AnyAsync(Ct)).ShouldBeFalse();
        var saved = await assert.Orders.SingleAsync(Ct);
        saved.Status.ShouldBe(OrderStatus.Cancelled);
        saved.CancelledDate.ShouldBe(order.CancelledDate);
        saved.CompletedDate.ShouldBe(order.CompletedDate);
    }

    [Fact]
    public async Task DeleteInvoiceAsync_Unknown_ReturnsNull()
    {
        var (order, _) = await SeedInvoicedOrderAsync();

        await using (var context = Db.CreateContext())
        {
            (
                await new InvoiceRepo(context).DeleteInvoiceAsync(TestData.Faker.Random.Guid())
            ).ShouldBeNull();
        }

        await using var assert = Db.CreateContext();
        (await assert.Invoices.SingleAsync(Ct)).OrderId.ShouldBe(order.Id);
        (await assert.Orders.SingleAsync(Ct)).Status.ShouldBe(OrderStatus.Completed);
    }

    [Fact]
    public async Task GetInvoiceCountAsync_CountsOnlyInvoices()
    {
        await SeedInvoicedOrderAsync();
        await SeedInvoicedOrderAsync(OrderStatus.Cancelled);
        await SeedOrderAsync();

        (await CountAsync(NoFilter)).ShouldBe(2);
    }
}
