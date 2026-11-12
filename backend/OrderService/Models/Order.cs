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

    // Changed only through POST /api/Orders/{id}/status (see Ordering/OrderLifecycle).
    [MaxLength(20)]
    public OrderStatus Status { get; set; } = OrderStatus.InProcess;

    // Set when the order is completed; cleared when its invoice is deleted.
    public DateTime? CompletedDate { get; set; }

    // Set when the order is cancelled; cleared when it is reopened.
    public DateTime? CancelledDate { get; set; }

    [MaxLength(500)]
    public string? ShippingAddress { get; set; }

    public Guid? CustomerId { get; set; }

    // The customer as CustomerService returned it when the order was created or its customer
    // was last changed; later edits in CustomerService don't reach it.
    public CustomerSnapshot? Customer { get; set; }
    public Invoice? Invoice { get; set; }

    // One item per product.
    public List<OrderItem> Items { get; set; } = [];
}
