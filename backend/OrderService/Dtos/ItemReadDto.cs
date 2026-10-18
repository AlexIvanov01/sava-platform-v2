using System;

namespace OrderService.Dtos;

public class ItemReadDto
{
    public Guid ExternalBatchId { get; set; }
    public Guid? ExternalProductId { get; set; }
    public string? Name { get; set; }
    public decimal? Price { get; set; }
    public string? Lot { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public int Quantity { get; set; }
}
