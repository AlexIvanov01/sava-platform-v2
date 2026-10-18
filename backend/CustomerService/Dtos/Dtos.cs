using System;
using System.ComponentModel.DataAnnotations;

namespace CustomerService.Dtos;

public record CustomerReadDto(Guid Id, string? Name, string? CompanyName,
        string? Email, string? PhoneNumber, string? Address, string? City,
        string? Country, string? BankName, string? IBAN, string? BIC, string? VATNumber, string? UIC);

public record CustomerCreateDto([Required] string Name, string CompanyName, string Email,
        string PhoneNumber, string Address, string City, string Country,
        string BankName, string IBAN, string BIC, string VATNumber, string UIC);

public record CustomerUpdateDto(string? Name, string? CompanyName, string? Email,
        string? PhoneNumber, string? Address, string? City, string? Country,
        string? BankName, string? IBAN, string? BIC, string? VATNumber, string? UIC);

