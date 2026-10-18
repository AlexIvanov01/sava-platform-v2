using CustomerService.Mappings;
using CustomerService.Models;

namespace CustomerService.Tests;

public class CustomerMappingsTests
{
    [Fact]
    public void ToReadDto_CopiesAllFields()
    {
        var customer = CustomerBuilder.Customer();

        var dto = customer.ToReadDto();

        dto.Id.ShouldBe(customer.Id);
        dto.Name.ShouldBe(customer.Name);
        dto.CompanyName.ShouldBe(customer.CompanyName);
        dto.Email.ShouldBe(customer.Email);
        dto.PhoneNumber.ShouldBe(customer.PhoneNumber);
        dto.Address.ShouldBe(customer.Address);
        dto.City.ShouldBe(customer.City);
        dto.Country.ShouldBe(customer.Country);
        dto.BankName.ShouldBe(customer.BankName);
        dto.IBAN.ShouldBe(customer.IBAN);
        dto.BIC.ShouldBe(customer.BIC);
        dto.VATNumber.ShouldBe(customer.VATNumber);
        dto.UIC.ShouldBe(customer.UIC);
    }

    [Fact]
    public void ToEntity_FromCreateDto_CopiesAllFieldsAndLeavesIdUnset()
    {
        var dto = CustomerBuilder.CreateDto();

        var customer = dto.ToEntity();

        customer.Id.ShouldBe(Guid.Empty);
        customer.CompanyName.ShouldBe(dto.CompanyName);
        customer.Email.ShouldBe(dto.Email);
        customer.Name.ShouldBe(dto.Name);
        customer.IBAN.ShouldBe(dto.IBAN);
        customer.UIC.ShouldBe(dto.UIC);
    }

    [Fact]
    public void ToEntity_FromUpdateDto_UsesRouteIdAndKeepsNulls()
    {
        var id = Guid.NewGuid();

        var customer = CustomerBuilder.UpdateDto(name: "New name").ToEntity(id);

        customer.Id.ShouldBe(id);
        customer.Name.ShouldBe("New name");
        customer.City.ShouldBeNull();
    }

    [Fact]
    public void ToPublishedDto_SetsExternalIdAndEvent()
    {
        var customer = CustomerBuilder.Customer();

        var dto = customer.ToPublishedDto("Customer_Updated");

        dto.ExternalId.ShouldBe(customer.Id);
        dto.Event.ShouldBe("Customer_Updated");
        dto.Name.ShouldBe(customer.Name);
        dto.VATNumber.ShouldBe(customer.VATNumber);
    }

    [Fact]
    public void ToGrpcModel_ReplacesNullsWithEmptyStrings()
    {
        var customer = new Customer { Id = Guid.NewGuid(), Name = "Only name" };

        var model = customer.ToGrpcModel();

        model.ExternalId.ShouldBe(customer.Id.ToString());
        model.Name.ShouldBe("Only name");
        model.CompanyName.ShouldBeEmpty();
        model.Email.ShouldBeEmpty();
        model.IBAN.ShouldBeEmpty();
        model.UIC.ShouldBeEmpty();
    }
}
