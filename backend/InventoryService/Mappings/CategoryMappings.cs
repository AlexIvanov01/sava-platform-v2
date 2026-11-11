using System;
using InventoryService.Dtos;
using InventoryService.Models;

namespace InventoryService.Mappings;

public static class CategoryMappings
{
    public static CategoryReadDto ToReadDto(this Category category, int productCount) =>
        new(category.Id, category.Name, category.Description, productCount, category.ToAuditDto());

    public static Category ToEntity(this CategoryCreateDto dto) =>
        new() { Name = dto.Name, Description = dto.Description };

    public static Category ToEntity(this CategoryUpdateDto dto, Guid id) =>
        new()
        {
            Id = id,
            Name = dto.Name,
            Description = dto.Description,
        };
}
