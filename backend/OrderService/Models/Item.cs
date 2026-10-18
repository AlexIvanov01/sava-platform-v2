using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace OrderService.Models;

public class Item
{
    [Key]
    public Guid ExternalBatchId { get; set; }
    public Guid? ExternalProductId { get; set; }

    [MaxLength(100)]
    public string? Name { get; set; }
    public decimal? Price { get; set; }

    [MaxLength(50)]
    public string? Lot { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public List<OrderItem>? ItemOrders { get; set; }
}
