using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OrderService.Dtos;
public record OrderReadDto(Guid Id, DateTime OrderDate, DateTime? ShippedDate,
    string? ShippingAddress, string? OrderStatus, Guid CustomerId,
    InvoiceReadDto? Invoice, List<OrderItemReadDto>? OrderItems);

public record CustomerReadDto(Guid ExternalId, string? Name, string? CompanyName,
     string? Email, string? PhoneNumber, string? Address, string? City,
    string? Country, string? BankName, string? IBAN, string? BIC, string? VATNumber, string? UIC);
    
public record InvoiceReadDto(int Id, DateTime InvoiceDate, Guid OrderId,
     string? InvoiceStatus, decimal TotalAmount);

public record OrderItemReadDto(Guid ItemId, int OrderItemQuantity);

public record OrderItemUpdateDto([Required] Guid ItemId, [Required]  [Range(1, 1000)] int OrderItemQuantity);

public record OrderItemPublishedDto(Guid ItemId, int OrderItemQuantity);
public record OrderItemCreateDto([Required] Guid ItemId, [Required] [Range(1, 1000)] int OrderItemQuantity);

public record InvoiceCreateDto(int? ID, DateTime? InvoiceDate,
     string InvoiceStatus, decimal? TotalAmount);

public record OrderCreateDto(DateTime? OrderDate, DateTime? ShippedDate, 
    string ShippingAddress, string OrderStatus, [Required] Guid? CustomerId,
    [Required] List<OrderItemCreateDto> OrderItems);

public record OrderUpdateDto(DateTime? OrderDate, DateTime? ShippedDate,
    string? ShippingAddress, string? OrderStatus, Guid? CustomerId, List<OrderItemUpdateDto>? OrderItems);

public record InvoiceUpdateDto(DateTime? InvoiceDate,
     string? InvoiceStatus, decimal? TotalAmount);

public record CustomerPublishedDto(Guid ExternalId, string? Name, string? CompanyName,
    string? Email, string? PhoneNumber, string? Address, string? City, string? Country,
    string? BankName, string? IBAN, string? BIC, string? VATNumber, string? UIC);
