using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ExceptionExtensions;
using OrderService.Controllers;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class InvoicesControllerTests
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

    private readonly IInvoiceRepo _repo = Substitute.For<IInvoiceRepo>();
    private readonly FakeTimeProvider _clock;
    private readonly InvoicesController _controller;

    public InvoicesControllerTests()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(TestData.Date(2026, 2027), TimeSpan.Zero));
        _controller = new InvoicesController(_repo, _clock);
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;
    private DateOnly Today => DateOnly.FromDateTime(Now);

    private static Invoice NewInvoice() =>
        OrderBuilder.Invoice(OrderBuilder.Order(OrderBuilder.Item()));

    private static ProblemDetails ShouldBeProblem(IConvertToActionResult result, int status)
    {
        var objectResult = result.Convert().ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(status);
        return objectResult.Value.ShouldBeOfType<ProblemDetails>();
    }

    [Fact]
    public async Task GetInvoices_PassesTheFilterAndTodayAndBuildsThePage()
    {
        var f = TestData.Faker;
        var cursor = f.Random.Guid();
        var from = DateOnly.FromDateTime(TestData.Date(2025, 2025));
        var to = from.AddDays(f.Random.Int(0, 60));
        var customerId = f.Random.Guid();
        var number = f.Random.ReplaceNumbers("####");
        var status = f.PickRandom<PaymentStatus>();
        var overdue = f.Random.Bool();
        var filter = new InvoiceFilter(from, to, customerId, number, status, overdue);
        var invoices = new[] { NewInvoice(), NewInvoice() };
        invoices[0].DueDate = Today.AddDays(-1).ToDateTime(TimeOnly.MinValue);
        invoices[0].PaymentStatus = PaymentStatus.Unpaid;
        var count = f.Random.Int(3, 95);
        _repo.GetInvoicesAsync(filter, Today, cursor, 2).Returns(invoices);
        _repo.GetInvoiceCountAsync(filter, Today).Returns(count);

        var result = await _controller.GetInvoices(
            cursor,
            from,
            to,
            customerId,
            number,
            status,
            overdue,
            2
        );

        var page = result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<InvoicePageReadDto>();
        var dtos = page.Invoices.ToList();
        dtos.Select(i => i.Id).ShouldBe(invoices.Select(i => i.Id));
        dtos[0].IsOverdue.ShouldBeTrue();
        dtos[0].OrderStatus.ShouldBe(invoices[0].Order!.Status);
        dtos[1].Number.ShouldBe(invoices[1].Number);
        page.Cursor.ShouldBe(invoices[1].Id);
        page.Pages.ShouldBe((count + 1) / 2);
        await _repo.Received(1).GetInvoicesAsync(filter, Today, cursor, 2);
        await _repo.Received(1).GetInvoiceCountAsync(filter, Today);
    }

    [Fact]
    public async Task GetInvoices_NoFilters_PassesAnEmptyFilter()
    {
        var empty = new InvoiceFilter(null, null, null, null, null, null);
        _repo.GetInvoicesAsync(empty, Today, null, 10).Returns([NewInvoice()]);
        _repo.GetInvoiceCountAsync(empty, Today).Returns(1);

        var page = (await _controller.GetInvoices(null, null, null, null, null, null, null))
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<InvoicePageReadDto>();

        page.Invoices.ShouldHaveSingleItem();
        page.Pages.ShouldBe(1);
    }

    [Fact]
    public async Task GetInvoices_Empty_ReturnsNotFound()
    {
        _repo.GetInvoicesAsync(Arg.Any<InvoiceFilter>(), Arg.Any<DateOnly>(), null, 10).Returns([]);

        (
            await _controller.GetInvoices(null, null, null, null, null, null, null)
        ).Result.ShouldBeOfType<NotFoundResult>();
        await _repo.DidNotReceiveWithAnyArgs().GetInvoiceCountAsync(default!, default);
    }

    [Fact]
    public async Task GetInvoices_RepoThrows_Returns500()
    {
        _repo
            .GetInvoicesAsync(
                Arg.Any<InvoiceFilter>(),
                Arg.Any<DateOnly>(),
                Arg.Any<Guid?>(),
                Arg.Any<int>()
            )
            .ThrowsAsync(new InvalidOperationException());

        (await _controller.GetInvoices(null, null, null, null, null, null, null))
            .Result.ShouldBeOfType<ObjectResult>()
            .StatusCode.ShouldBe(500);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CheckNumber_ReportsWhetherTakenAndTheNextNumber(bool taken)
    {
        var number = OrderBuilder.InvoiceNumber();
        var next = OrderBuilder.InvoiceNumber();
        _repo.NumberExistsAsync(number).Returns(taken);
        _repo.NextNumberAsync().Returns(next);

        (await _controller.CheckNumber(number))
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBe(new InvoiceNumberCheckDto(number, taken, next));
    }

    [Fact]
    public async Task CheckNumber_RepoThrows_Returns500()
    {
        _repo.NumberExistsAsync(Arg.Any<string>()).ThrowsAsync(new InvalidOperationException());

        (await _controller.CheckNumber(OrderBuilder.InvoiceNumber()))
            .Result.ShouldBeOfType<ObjectResult>()
            .StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task GetInvoiceById_MissingAndExisting()
    {
        var invoice = NewInvoice();
        invoice.DueDate = Today.AddDays(-1).ToDateTime(new TimeOnly(12, 0));
        _repo.GetInvoiceByIdAsync(invoice.Id).Returns(invoice);

        (
            await _controller.GetInvoiceById(TestData.Faker.Random.Guid())
        ).Result.ShouldBeOfType<NotFoundResult>();
        var dto = (await _controller.GetInvoiceById(invoice.Id))
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<InvoiceReadDto>();

        dto.Id.ShouldBe(invoice.Id);
        dto.OrderId.ShouldBe(invoice.OrderId);
        dto.Number.ShouldBe(invoice.Number);
        dto.IsOverdue.ShouldBeTrue();
        dto.Gross.ShouldBe(invoice.Gross);
        dto.Items.Count.ShouldBe(invoice.Items.Count);
    }

    [Fact]
    public async Task GetInvoiceById_DueToday_IsNotOverdue()
    {
        var invoice = NewInvoice();
        invoice.DueDate = Today.ToDateTime(TimeOnly.MinValue);
        _repo.GetInvoiceByIdAsync(invoice.Id).Returns(invoice);

        (await _controller.GetInvoiceById(invoice.Id))
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<InvoiceReadDto>()
            .IsOverdue.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateInvoice_Missing_Returns404WithoutSaving()
    {
        (
            await _controller.UpdateInvoice(TestData.Faker.Random.Guid(), NoChanges)
        ).Result.ShouldBeOfType<NotFoundResult>();
        await _repo.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateInvoice_Paid_SavesAndReturnsTheInvoiceWithTodayAsPaidDate()
    {
        var invoice = NewInvoice();
        _repo.GetInvoiceForUpdateAsync(invoice.Id).Returns(invoice);
        var dto = NoChanges with
        {
            Number = OrderBuilder.InvoiceNumber(),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Paid,
        };

        var body = (await _controller.UpdateInvoice(invoice.Id, dto))
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<InvoiceReadDto>();

        body.Id.ShouldBe(invoice.Id);
        body.Number.ShouldBe(dto.Number);
        body.PaymentMethod.ShouldBe(PaymentMethod.Cash);
        body.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        body.PaidDate.ShouldBe(Now.Date);
        body.IsOverdue.ShouldBeFalse();
        invoice.PaidDate.ShouldBe(Now.Date);
        await _repo.Received(1).SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateInvoice_PaidDateOnUnpaid_Returns400WithoutSaving()
    {
        var invoice = NewInvoice();
        var number = invoice.Number;
        _repo.GetInvoiceForUpdateAsync(invoice.Id).Returns(invoice);

        var problem = ShouldBeProblem(
            await _controller.UpdateInvoice(
                invoice.Id,
                NoChanges with
                {
                    Number = OrderBuilder.InvoiceNumber(),
                    PaidDate = TestData.Date(2026, 2026),
                }
            ),
            400
        );

        problem.Title.ShouldBe("Invalid invoice update");
        problem.Detail.ShouldBe("PaidDate can only be set on a Paid invoice.");
        invoice.Number.ShouldBe(number);
        invoice.PaidDate.ShouldBeNull();
        await _repo.DidNotReceive().SaveChangesAsync();
    }

    [Fact]
    public async Task UpdateInvoice_SaveThrows_Returns500()
    {
        var invoice = NewInvoice();
        _repo.GetInvoiceForUpdateAsync(invoice.Id).Returns(invoice);
        _repo.SaveChangesAsync().ThrowsAsync(new InvalidOperationException());

        (
            await _controller.UpdateInvoice(
                invoice.Id,
                NoChanges with
                {
                    PaymentStatus = PaymentStatus.Paid,
                }
            )
        )
            .Result.ShouldBeOfType<ObjectResult>()
            .StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task DeleteInvoice_MissingAndExisting()
    {
        var invoice = NewInvoice();
        _repo.DeleteInvoiceAsync(invoice.Id).Returns(invoice);

        (
            await _controller.DeleteInvoice(TestData.Faker.Random.Guid())
        ).ShouldBeOfType<NotFoundResult>();
        (await _controller.DeleteInvoice(invoice.Id)).ShouldBeOfType<NoContentResult>();
        await _repo.Received(1).DeleteInvoiceAsync(invoice.Id);
    }

    [Fact]
    public async Task DeleteInvoice_RepoThrows_Returns500()
    {
        _repo.DeleteInvoiceAsync(Arg.Any<Guid>()).ThrowsAsync(new InvalidOperationException());

        (await _controller.DeleteInvoice(TestData.Faker.Random.Guid()))
            .ShouldBeOfType<ObjectResult>()
            .StatusCode.ShouldBe(500);
    }
}
