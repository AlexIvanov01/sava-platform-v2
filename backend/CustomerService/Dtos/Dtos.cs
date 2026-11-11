using System;
using System.Collections.Generic;
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
    // True while any order references the customer; such customers can't be deleted.
    bool HasOrders,
    AuditReadDto Audit
);

public record CustomerPageReadDto(
    IEnumerable<CustomerReadDto> CustomerReadDtos,
    Guid Cursor,
    int Pages
);

// Query-string filters for GET /api/Customers; null fields don't filter.
public record CustomerFilter(string? Name = null, string? UIC = null, string? VATNumber = null)
{
    public static readonly CustomerFilter None = new();
}

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
