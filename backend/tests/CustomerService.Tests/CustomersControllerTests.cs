using CustomerService.AsyncDataServices;
using CustomerService.Controllers;
using CustomerService.DataAccess;
using CustomerService.Dtos;
using CustomerService.Models;
using Microsoft.AspNetCore.Mvc;
using NSubstitute.ExceptionExtensions;

namespace CustomerService.Tests;

public class CustomersControllerTests
{
    private readonly ICustomerRepo _repo = Substitute.For<ICustomerRepo>();
    private readonly IMessageBusClient _bus = Substitute.For<IMessageBusClient>();
    private readonly CustomersController _controller;

    public CustomersControllerTests() => _controller = new CustomersController(_repo, _bus);

    [Fact]
    public async Task GetCustomers_NoCustomers_ReturnsNotFound()
    {
        _repo.GetCustomersAsync(CustomerFilter.None, null, 10).Returns([]);

        var result = await _controller.GetCustomers(null);

        result.Result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetCustomers_ReturnsPageWithFiltersCursorAndPageCount()
    {
        var a = CustomerBuilder.Customer();
        var b = CustomerBuilder.Customer();
        var filter = new CustomerFilter("Ltd", "201", "BG2");
        _repo.GetCustomersAsync(filter, null, 2).Returns([(a, true), (b, false)]);
        _repo.GetCustomersCountAsync(filter).Returns(5);

        var result = await _controller.GetCustomers(null, 2, "Ltd", "201", "BG2");

        var page = result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<CustomerPageReadDto>();
        page.CustomerReadDtos.Select(c => (c.Id, c.HasOrders))
            .ShouldBe([(a.Id, true), (b.Id, false)]);
        page.Cursor.ShouldBe(b.Id);
        page.Pages.ShouldBe(3);
    }

    [Fact]
    public async Task GetCustomers_RepoThrows_Returns500()
    {
        _repo
            .GetCustomersAsync(Arg.Any<CustomerFilter>(), Arg.Any<Guid?>(), Arg.Any<int>())
            .ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _controller.GetCustomers(null);

        result.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task GetCustomerById_Missing_ReturnsNotFound()
    {
        var result = await _controller.GetCustomerById(Guid.NewGuid());

        result.Result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetCustomerById_Existing_ReturnsOk()
    {
        var customer = CustomerBuilder.Customer();
        _repo.GetCustomerAsync(customer.Id).Returns(customer);
        _repo.HasOrdersAsync(customer.Id).Returns(true);

        var result = await _controller.GetCustomerById(customer.Id);

        var dto = result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<CustomerReadDto>();
        dto.Name.ShouldBe(customer.Name);
        dto.HasOrders.ShouldBeTrue();
    }

    [Fact]
    public async Task CreateCustomer_SavesAndPublishesCustomerPublished()
    {
        var dto = CustomerBuilder.CreateDto();

        var result = await _controller.CreateCustomer(dto);

        var created = result.Result.ShouldBeOfType<CreatedAtRouteResult>();
        created.RouteName.ShouldBe(nameof(CustomersController.GetCustomerById));
        created.Value.ShouldBeOfType<CustomerReadDto>().Name.ShouldBe(dto.Name);
        await _repo.Received(1).CreateCustomerAsync(Arg.Is<Customer>(c => c.Name == dto.Name));
        await _bus.Received(1)
            .PublishNewCustomerAsync(
                Arg.Is<CustomerPublishedDto>(p =>
                    p.Event == "Customer_Published" && p.Name == dto.Name
                )
            );
    }

    [Fact]
    public async Task CreateCustomer_RepoThrows_Returns500AndDoesNotPublish()
    {
        _repo.CreateCustomerAsync(Arg.Any<Customer>()).ThrowsAsync(new InvalidOperationException());

        var result = await _controller.CreateCustomer(CustomerBuilder.CreateDto());

        result.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(500);
        await _bus.DidNotReceiveWithAnyArgs().PublishNewCustomerAsync(default!);
    }

    [Fact]
    public async Task CreateCustomer_PublishFails_StillReturnsCreated()
    {
        _bus.PublishNewCustomerAsync(Arg.Any<CustomerPublishedDto>())
            .ThrowsAsync(new InvalidOperationException());

        var result = await _controller.CreateCustomer(CustomerBuilder.CreateDto());

        result.Result.ShouldBeOfType<CreatedAtRouteResult>();
    }

    [Fact]
    public async Task UpdateCustomer_Missing_ReturnsNotFoundAndDoesNotPublish()
    {
        var result = await _controller.UpdateCustomer(
            Guid.NewGuid(),
            CustomerBuilder.UpdateDto("x")
        );

        result.Result.ShouldBeOfType<NotFoundResult>();
        await _bus.DidNotReceiveWithAnyArgs().PublishNewCustomerAsync(default!);
    }

    [Fact]
    public async Task UpdateCustomer_Existing_ReturnsSavedCustomerAndPublishesIt()
    {
        var saved = CustomerBuilder.Customer("New name");
        var id = saved.Id;
        _repo.UpdateCustomerAsync(Arg.Any<Customer>()).Returns(saved);

        var result = await _controller.UpdateCustomer(id, CustomerBuilder.UpdateDto("New name"));

        var body = result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<CustomerReadDto>();
        body.Name.ShouldBe("New name");
        body.Email.ShouldBe(saved.Email);
        await _repo
            .Received(1)
            .UpdateCustomerAsync(Arg.Is<Customer>(c => c.Id == id && c.Name == "New name"));
        await _bus.Received(1)
            .PublishNewCustomerAsync(
                Arg.Is<CustomerPublishedDto>(p =>
                    p.Event == "Customer_Updated" && p.ExternalId == id && p.Email == saved.Email
                )
            );
    }

    [Fact]
    public async Task DeleteCustomer_Missing_ReturnsNotFound()
    {
        _repo.DeleteCustomerAsync(Arg.Any<Guid>()).Returns((DeleteCustomerStatus.NotFound, null));

        var result = await _controller.DeleteCustomer(Guid.NewGuid());

        result.ShouldBeOfType<NotFoundResult>();
        await _bus.DidNotReceiveWithAnyArgs().PublishNewCustomerAsync(default!);
    }

    [Fact]
    public async Task DeleteCustomer_Existing_ReturnsNoContentAndPublishesCustomerDeleted()
    {
        var customer = CustomerBuilder.Customer();
        _repo.DeleteCustomerAsync(customer.Id).Returns((DeleteCustomerStatus.Deleted, customer));

        var result = await _controller.DeleteCustomer(customer.Id);

        result.ShouldBeOfType<NoContentResult>();
        await _bus.Received(1)
            .PublishNewCustomerAsync(
                Arg.Is<CustomerPublishedDto>(p =>
                    p.Event == "Customer_Deleted" && p.ExternalId == customer.Id
                )
            );
    }

    [Fact]
    public async Task DeleteCustomer_WithOrders_Returns409AndDoesNotPublish()
    {
        var customer = CustomerBuilder.Customer();
        _repo.DeleteCustomerAsync(customer.Id).Returns((DeleteCustomerStatus.HasOrders, customer));

        var result = await _controller.DeleteCustomer(customer.Id);

        var problem = result.ShouldBeOfType<ObjectResult>().Value.ShouldBeOfType<ProblemDetails>();
        problem.Status.ShouldBe(409);
        problem.Title.ShouldBe("Customer has orders");
        await _bus.DidNotReceiveWithAnyArgs().PublishNewCustomerAsync(default!);
    }
}
