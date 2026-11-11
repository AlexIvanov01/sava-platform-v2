using System;
using System.ComponentModel.DataAnnotations;

namespace CustomerService.Dtos;

public record AuditReadDto(
    DateTime? CreatedAt,
    DateTime? UpdatedAt,
    string? CreatedBy,
    string? UpdatedBy
);

public record CustomerReadDto(
    Guid Id,
    string? Name,
    string? CompanyName,
    string? Email,
    string? PhoneNumber,
    string? Address,
    string? City,
    string? Country,
    string? BankName,
    string? IBAN,
    string? BIC,
    string? VATNumber,
    string? UIC,
    AuditReadDto Audit
);

public record CustomerCreateDto(
    [Required] string Name,
    string CompanyName,
    string Email,
    string PhoneNumber,
    string Address,
    string City,
    string Country,
    string BankName,
    string IBAN,
    string BIC,
    string VATNumber,
    string UIC
);

public record CustomerUpdateDto(
    string? Name,
    string? CompanyName,
    string? Email,
    string? PhoneNumber,
    string? Address,
    string? City,
    string? Country,
    string? BankName,
    string? IBAN,
    string? BIC,
    string? VATNumber,
    string? UIC
);
