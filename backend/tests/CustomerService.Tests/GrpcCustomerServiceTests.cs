using CustomerService.DataAccess;
using CustomerService.Models;
using CustomerService.SyncDataServices.Grpc;

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
}
