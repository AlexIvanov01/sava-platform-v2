using Bogus;
using Bogus.Extensions;
using CustomerService.Dtos;
using CustomerService.Models;
using Sava.Tests.Shared;

namespace CustomerService.Tests;

// Fake data from Bogus; lengths are clamped to the model's [MaxLength] limits.
internal static class CustomerBuilder
{
    public static Customer Customer(string? name = null)
    {
        var f = TestData.Faker;
        return new()
        {
            Id = f.Random.Guid(),
            Name = name ?? f.Name.FullName(),
            CompanyName = f.Company.CompanyName(),
            Email = f.Internet.Email().ClampLength(max: 100),
            PhoneNumber = f.Phone.PhoneNumber("+359#########"),
            Address = f.Address.StreetAddress(),
            City = f.Address.City(),
            Country = f.Address.Country(),
            BankName = $"{f.Company.CompanyName()} Bank".ClampLength(max: 100),
            IBAN = f.Finance.Iban(),
            BIC = f.Finance.Bic(),
            VATNumber = f.Random.Replace("BG#########"),
            UIC = f.Random.Replace("#########"),
        };
    }

    public static CustomerCreateDto CreateDto(string? name = null)
    {
        var c = Customer(name);
        return new(
            c.Name!,
            c.CompanyName!,
            c.Email!,
            c.PhoneNumber!,
            c.Address!,
            c.City!,
            c.Country!,
            c.BankName!,
            c.IBAN!,
            c.BIC!,
            c.VATNumber!,
            c.UIC!
        );
    }

    public static CustomerUpdateDto UpdateDto(string? name = null, string? city = null) =>
        new(name, null, null, null, null, city, null, null, null, null, null, null);
}
