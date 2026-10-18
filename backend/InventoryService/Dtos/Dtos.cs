using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace InventoryService.Dtos;

public record BatchReadDto(Guid Id, string? Lot, int Quantity, decimal PurchasePrice, decimal SellPrice, DateTime ExpirationDate);

public record ProductReadDto(Guid Id, string? Name, string? Description, string? Brand, string? Supplier,
    string? ImageURL, string? Status, string? Barcode, int ReorderLevel, float Weight, List<BatchReadDto> Batches);

public record ProductCreateDto([Required] string Name, string? Description,[Required] string Brand, [Required] string Supplier,
    string? ImageURL, string? Status, string? Barcode, int? ReorderLevel, float? Weight);

public record BatchCreateDto([Required] string Lot, [Required] int? Quantity, [Required] decimal? PurchasePrice,
    [Required] decimal? SellPrice, [Required] DateTime? ExpirationDate);

public record ProductUpdateDto(string? Name, string? Description, string? Brand, string? Supplier,
     string? ImageURL, string? Status, string? Barcode, int? ReorderLevel, float? Weight);

public record BatchUpdateDto(string? Lot, int? Quantity, decimal? PurchasePrice,
    decimal? SellPrice, DateTime? ExpirationDate);

public record OrderPublishedDto(IEnumerable<OrderItemPublishedDto> OrderItems, string Event);

public record OrderItemPublishedDto(Guid ItemId, int OrderItemQuantity);
