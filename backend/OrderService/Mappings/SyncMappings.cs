using System;
using CustomerService;
using InventoryService;
using OrderService.Dtos;
using OrderService.Models;

namespace OrderService.Mappings;

// Maps data synced from InventoryService and CustomerService (RabbitMQ events and gRPC)
// into OrderService's local Item and Customer copies.
public static class SyncMappings
{
    public static Item ToEntity(this ItemPublishedDto dto) => new()
    {
        ExternalBatchId = dto.ExternalBatchId.GetValueOrDefault(),
        ExternalProductId = dto.ExternalProductId,
        Name = dto.Name,
        Price = dto.Price,
        Lot = dto.Lot,
        ExpirationDate = dto.ExpirationDate
    };

    public static Item ToEntity(this GrpcInventoryModel model) => new()
    {
        ExternalBatchId = Guid.Parse(model.ExternalBatchId),
        ExternalProductId = Guid.Parse(model.ExternalProductId),
        Name = model.Name,
        Price = decimal.Parse(model.Price),
        Lot = model.Lot,
        ExpirationDate = DateTime.Parse(model.ExpirationDate)
    };

    public static Customer ToEntity(this CustomerPublishedDto dto) => new()
    {
        ExternalId = dto.ExternalId,
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
        UIC = dto.UIC
    };

    public static Customer ToEntity(this GrpcCustomerModel model) => new()
    {
        ExternalId = Guid.Parse(model.ExternalId),
        Name = model.Name,
        CompanyName = model.CompanyName,
        Email = model.Email,
        PhoneNumber = model.PhoneNumber,
        Address = model.Address,
        City = model.City,
        Country = model.Country,
        BankName = model.BankName,
        IBAN = model.IBAN,
        BIC = model.BIC,
        VATNumber = model.VATNumber,
        UIC = model.UIC
    };

    public static CustomerReadDto ToReadDto(this Customer customer) => new(
        customer.ExternalId, customer.Name, customer.CompanyName, customer.Email, customer.PhoneNumber,
        customer.Address, customer.City, customer.Country, customer.BankName, customer.IBAN,
        customer.BIC, customer.VATNumber, customer.UIC);
}
