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

    // The customer as CustomerService returned it when the order was created or its customer
    // was last changed; later edits in CustomerService don't reach it.
    public CustomerSnapshot? Customer { get; set; }
    public Invoice? Invoice { get; set; }

    // One item per product.
    public List<OrderItem> Items { get; set; } = [];
}
