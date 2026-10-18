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
        _repo.GetAllCustomersAsync().Returns(new List<Customer>());

        var result = await _controller.GetCustomers();

        result.Result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetCustomers_WithCustomers_ReturnsOkWithDtos()
    {
        var customer = CustomerBuilder.Customer();
        _repo.GetAllCustomersAsync().Returns(new List<Customer> { customer });

        var result = await _controller.GetCustomers();

        var dtos = result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeAssignableTo<IEnumerable<CustomerReadDto>>();
        dtos.ShouldHaveSingleItem().Id.ShouldBe(customer.Id);
    }

    [Fact]
    public async Task GetCustomers_RepoThrows_Returns500()
    {
        _repo.GetAllCustomersAsync().ThrowsAsync(new InvalidOperationException("db down"));

        var result = await _controller.GetCustomers();

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

        var result = await _controller.GetCustomerById(customer.Id);

        result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<CustomerReadDto>()
            .Name.ShouldBe(customer.Name);
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
        var result = await _controller.DeleteCustomer(Guid.NewGuid());

        result.ShouldBeOfType<NotFoundResult>();
        await _bus.DidNotReceiveWithAnyArgs().PublishNewCustomerAsync(default!);
    }

    [Fact]
    public async Task DeleteCustomer_Existing_ReturnsNoContentAndPublishesCustomerDeleted()
    {
        var customer = CustomerBuilder.Customer();
        _repo.DeleteCustomerAsync(customer.Id).Returns(customer);

        var result = await _controller.DeleteCustomer(customer.Id);

        result.ShouldBeOfType<NoContentResult>();
        await _bus.Received(1)
            .PublishNewCustomerAsync(
                Arg.Is<CustomerPublishedDto>(p =>
                    p.Event == "Customer_Deleted" && p.ExternalId == customer.Id
                )
            );
    }
}
