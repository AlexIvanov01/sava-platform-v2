using System;
using System.Collections.Generic;

namespace OrderService.Dtos;

public class InvoicePageReadDto
{
    public IEnumerable<InvoiceReadDto> Invoices { get; set; } = [];
    public Guid Cursor { get; set; }
    public int Pages { get; set; }
}
