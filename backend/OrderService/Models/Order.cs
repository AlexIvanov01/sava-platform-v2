using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OrderService.Models;

public class Order : AuditableEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }

    public DateTime? OrderDate { get; set; }

    public DateTime? ShippedDate { get; set; }

    [MaxLength(500)]
    public string? ShippingAddress { get; set; }

    [MaxLength(50)]
    public string? OrderStatus { get; set; }

    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Invoice? Invoice { get; set; }
    public List<OrderItem> OrderItems { get; set; } = [];
}
