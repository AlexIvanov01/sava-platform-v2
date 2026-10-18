using System;
using CustomerService.Dtos;
using CustomerService.Models;

namespace CustomerService.Mappings;

public static class CustomerMappings
{
    public static CustomerReadDto ToReadDto(this Customer customer) =>
        new(
            customer.Id,
            customer.Name,
            customer.CompanyName,
            customer.Email,
            customer.PhoneNumber,
            customer.Address,
            customer.City,
            customer.Country,
            customer.BankName,
            customer.IBAN,
            customer.BIC,
            customer.VATNumber,
            customer.UIC
        );

    public static Customer ToEntity(this CustomerCreateDto dto) =>
        new()
        {
            Name = dto.Name,
            CompanyName = dto.CompanyName,
            Email = dto.Email,
            PhoneNumber = dto.PhoneNumber,
            Address = dto.Address,
            City = dto.City,
            Country = dto.Country,
            BankName = dto.BankName,
            IBAN = dto.IBAN,
            BIC = dto.BIC,
            VATNumber = dto.VATNumber,
            UIC = dto.UIC,
        };

    public static Customer ToEntity(this CustomerUpdateDto dto, Guid id) =>
        new()
        {
            Id = id,
            Name = dto.Name,
            CompanyName = dto.CompanyName,
            Email = dto.Email,
            PhoneNumber = dto.PhoneNumber,
            Address = dto.Address,
            City = dto.City,
            Country = dto.Country,
            BankName = dto.BankName,
            IBAN = dto.IBAN,
            BIC = dto.BIC,
            VATNumber = dto.VATNumber,
            UIC = dto.UIC,
        };

    public static CustomerPublishedDto ToPublishedDto(this Customer customer, string eventName) =>
        new()
        {
            ExternalId = customer.Id,
            Name = customer.Name,
            CompanyName = customer.CompanyName,
            Email = customer.Email,
            PhoneNumber = customer.PhoneNumber,
            Address = customer.Address,
            City = customer.City,
            Country = customer.Country,
            BankName = customer.BankName,
            IBAN = customer.IBAN,
            BIC = customer.BIC,
            VATNumber = customer.VATNumber,
            UIC = customer.UIC,
            Event = eventName,
        };

    // Protobuf string fields reject null, so missing values are sent as empty strings.
    public static GrpcCustomerModel ToGrpcModel(this Customer customer) =>
        new()
        {
            ExternalId = customer.Id.ToString(),
            Name = customer.Name ?? string.Empty,
            CompanyName = customer.CompanyName ?? string.Empty,
            Email = customer.Email ?? string.Empty,
            PhoneNumber = customer.PhoneNumber ?? string.Empty,
            Address = customer.Address ?? string.Empty,
            City = customer.City ?? string.Empty,
            Country = customer.Country ?? string.Empty,
            BankName = customer.BankName ?? string.Empty,
            IBAN = customer.IBAN ?? string.Empty,
            BIC = customer.BIC ?? string.Empty,
            VATNumber = customer.VATNumber ?? string.Empty,
            UIC = customer.UIC ?? string.Empty,
        };
}
