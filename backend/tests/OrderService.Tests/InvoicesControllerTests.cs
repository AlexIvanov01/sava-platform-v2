using Microsoft.AspNetCore.Mvc;
using NSubstitute.ExceptionExtensions;
using OrderService.Controllers;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.Models;

namespace OrderService.Tests;

public class InvoicesControllerTests
{
    private readonly IInvoiceRepo _repo = Substitute.For<IInvoiceRepo>();
    private readonly InvoicesController _controller;

    public InvoicesControllerTests() => _controller = new InvoicesController(_repo);

    private static Invoice Invoice(int id)
    {
        var order = OrderBuilder.Order(OrderBuilder.Customer(), (OrderBuilder.Item(), 1));
        var invoice = new Invoice
        {
            Id = id,
            Order = order,
            OrderId = order.Id,
            InvoiceStatus = "Draft",
        };
        order.Invoice = invoice;
        return invoice;
    }

    [Fact]
    public async Task GetInvoices_ReturnsPageWithLastInvoiceIdAsCursor()
    {
        _repo.GetAllInvociesAsync(5, 2).Returns([Invoice(6), Invoice(8)]);
        _repo.GetInvoiceCountAsync().Returns(5);

        var result = await _controller.GetInvoices(5, 2);

        var page = result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<InvoicePageReadDto>();
        page.OrderReadDtos.ShouldNotBeNull().Count().ShouldBe(2);
        page.Cursor.ShouldBe(8);
        page.Pages.ShouldBe(3);
    }

    [Fact]
    public async Task GetInvoices_Empty_ReturnsNotFound()
    {
        _repo.GetAllInvociesAsync(null, 10).Returns([]);

        (await _controller.GetInvoices(null)).Result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetInvoices_RepoThrows_Returns500()
    {
        _repo
            .GetAllInvociesAsync(Arg.Any<int?>(), Arg.Any<int>())
            .ThrowsAsync(new InvalidOperationException());

        (await _controller.GetInvoices(null))
            .Result.ShouldBeOfType<ObjectResult>()
            .StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task GetInvoiceById_MissingAndExisting()
    {
        var invoice = Invoice(4);
        _repo.GetInvocieByIdAsync(4).Returns(invoice);

        (await _controller.GetInvoiceById(1)).Result.ShouldBeOfType<NotFoundResult>();
        var dto = (await _controller.GetInvoiceById(4))
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderFullReadDto>();
        dto.Id.ShouldBe(invoice.OrderId);
        dto.Invoice.ShouldNotBeNull().Id.ShouldBe(4);
    }

    [Fact]
    public async Task CreateInvoice_RepoRejects_ReturnsNotFound()
    {
        var result = await _controller.CreateInvoice(
            new InvoiceCreateDto(null, null, "Draft", 1m),
            Guid.NewGuid()
        );

        result.Result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task CreateInvoice_Valid_ReturnsCreated()
    {
        var orderId = Guid.NewGuid();
        _repo.CreateInvocieAsync(Arg.Any<Invoice>(), orderId).Returns(ci => ci.Arg<Invoice>());

        var result = await _controller.CreateInvoice(
            new InvoiceCreateDto(null, null, "Draft", 1m),
            orderId
        );

        var created = result.Result.ShouldBeOfType<CreatedAtRouteResult>();
        created.RouteName.ShouldBe(nameof(InvoicesController.GetInvoiceById));
        created.Value.ShouldBeOfType<InvoiceReadDto>().InvoiceStatus.ShouldBe("Draft");
    }

    [Fact]
    public async Task UpdateInvoice_MissingAndExisting_ReturnsSavedInvoice()
    {
        var saved = new Invoice
        {
            Id = 2,
            OrderId = Guid.NewGuid(),
            InvoiceStatus = "Paid",
            TotalAmount = 15m,
        };
        _repo.UpdateInvoiceAsync(Arg.Is<Invoice>(i => i.Id == 2)).Returns(saved);

        (
            await _controller.UpdateInvoice(1, new InvoiceUpdateDto(null, "Paid", null))
        ).Result.ShouldBeOfType<NotFoundResult>();
        var body = (await _controller.UpdateInvoice(2, new InvoiceUpdateDto(null, "Paid", null)))
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<InvoiceReadDto>();
        body.InvoiceStatus.ShouldBe("Paid");
        body.TotalAmount.ShouldBe(15m);
        body.OrderId.ShouldBe(saved.OrderId);
    }

    [Fact]
    public async Task UpdateInvoice_RepoThrows_Returns500()
    {
        _repo.UpdateInvoiceAsync(Arg.Any<Invoice>()).ThrowsAsync(new InvalidOperationException());

        (await _controller.UpdateInvoice(1, new InvoiceUpdateDto(null, "Paid", null)))
            .Result.ShouldBeOfType<ObjectResult>()
            .StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task DeleteInvoice_MissingAndExisting()
    {
        _repo.DeleteInvoiceAsync(2).Returns(Invoice(2));

        (await _controller.DeleteInvoice(1)).ShouldBeOfType<NotFoundResult>();
        (await _controller.DeleteInvoice(2)).ShouldBeOfType<NoContentResult>();
    }
}
