using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CustomerService.Models;

// Which customer an order belongs to, kept from OrderService's order events. Customers with
// orders can't be deleted. No foreign key: it records a fact owned by OrderService.
public class CustomerOrder
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid OrderId { get; set; }

    public Guid CustomerId { get; set; }
}
