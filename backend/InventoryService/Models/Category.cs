using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace InventoryService.Models;

public class Category : AuditableEntity
{
    [Key]
    public Guid Id { get; set; }

    [MaxLength(100)]
    public string? Name { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public List<Product> Products { get; set; } = [];
}
