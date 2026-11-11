using System;

namespace CustomerService.Dtos;

// The part of OrderService's order events this service needs.
public record OrderPublishedDto(Guid? OrderId, Guid? CustomerId, string Event);
