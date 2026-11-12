using CustomerService.DataAccess;
using CustomerService.Models;
using CustomerService.SyncDataServices.Grpc;
using Grpc.Core;
using Sava.Tests.Shared;

namespace CustomerService.Tests;

public class GrpcCustomerServiceTests
{
    [Fact]
    public async Task GetAllCustomers_ReturnsEveryCustomer()
    {
        var repo = Substitute.For<ICustomerRepo>();
        var customers = new List<Customer>
        {
            CustomerBuilder.Customer("A"),
            CustomerBuilder.Customer("B"),
        };
        repo.GetAllCustomersAsync().Returns(customers);

        var response = await new GrpcCustomerService(repo).GetAllCustomers(
            new GetAllRequest(),
            null!
        );

        response.Customers.Select(c => c.Name).ShouldBe(["A", "B"]);
        response.Customers[0].ExternalId.ShouldBe(customers[0].Id.ToString());
    }

    [Fact]
    public async Task GetCustomer_ReturnsTheCustomer()
    {
        var repo = Substitute.For<ICustomerRepo>();
        var customer = CustomerBuilder.Customer();
        repo.GetCustomerAsync(customer.Id).Returns(customer);

        var model = await new GrpcCustomerService(repo).GetCustomer(
            new GetCustomerRequest { Id = customer.Id.ToString() },
            null!
        );

        model.ExternalId.ShouldBe(customer.Id.ToString());
        model.Name.ShouldBe(customer.Name);
        model.VATNumber.ShouldBe(customer.VATNumber);
    }

    [Fact]
    public async Task GetCustomer_Unknown_IsNotFound()
    {
        var repo = Substitute.For<ICustomerRepo>();
        var id = TestData.Faker.Random.Guid();
        repo.GetCustomerAsync(id).Returns((Customer?)null);

        var ex = await Should.ThrowAsync<RpcException>(() =>
            new GrpcCustomerService(repo).GetCustomer(
                new GetCustomerRequest { Id = id.ToString() },
                null!
            )
        );

        ex.StatusCode.ShouldBe(StatusCode.NotFound);
    }

    [Fact]
    public async Task GetCustomer_InvalidId_IsInvalidArgument()
    {
        var ex = await Should.ThrowAsync<RpcException>(() =>
            new GrpcCustomerService(Substitute.For<ICustomerRepo>()).GetCustomer(
                new GetCustomerRequest { Id = "nope" },
                null!
            )
        );

        ex.StatusCode.ShouldBe(StatusCode.InvalidArgument);
    }
}
