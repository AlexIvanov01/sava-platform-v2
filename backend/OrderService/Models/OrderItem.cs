using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace OrderService.Models;

public class OrderItem
{
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = new Order();

    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid ItemId { get; set; }
    public Item Item { get; set; } = new Item();
    public int OrderItemQuantity { get; set; }
}
