using System;
using System.Collections.Generic;
using OrderService.Models;

namespace OrderService.Dtos;

public class OrderFullReadDto
{
    public Guid Id { get; set; }

    public DateTime? OrderDate { get; set; }

    public DateTime? ShippedDate { get; set; }
    public string? ShippingAddress { get; set; }
    public string? OrderStatus { get; set; }
    public CustomerReadDto? Customer { get; set; }
    public InvoiceReadDto? Invoice { get; set; }
    public List<ItemReadDto> Items { get; set; } = [];
}
