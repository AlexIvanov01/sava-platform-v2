using CustomerService.Dtos;
using CustomerService.Models;

namespace CustomerService.Tests;

internal static class CustomerBuilder
{
    public static Customer Customer(string name = "Ivan Petrov") =>
        new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            CompanyName = "Petrov EOOD",
            Email = "ivan@example.com",
            PhoneNumber = "+359888123456",
            Address = "1 Vitosha Blvd",
            City = "Sofia",
            Country = "Bulgaria",
            BankName = "UBB",
            IBAN = "BG80BNBG96611020345678",
            BIC = "UBBSBGSF",
            VATNumber = "BG123456789",
            UIC = "123456789",
        };

    public static CustomerCreateDto CreateDto(string name = "Ivan Petrov") =>
        new(
            name,
            "Petrov EOOD",
            "ivan@example.com",
            "+359888123456",
            "1 Vitosha Blvd",
            "Sofia",
            "Bulgaria",
            "UBB",
            "BG80BNBG96611020345678",
            "UBBSBGSF",
            "BG123456789",
            "123456789"
        );

    public static CustomerUpdateDto UpdateDto(string? name = null, string? city = null) =>
        new(name, null, null, null, null, city, null, null, null, null, null, null);
}
