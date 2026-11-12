using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace InventoryService.Dtos;

// OrderId: when editing an order, the stock it already holds counts as available to it.
public record AllocationSuggestRequestDto(
    Guid? OrderId,
    [Required] [MinLength(1)] List<AllocationItemRequestDto>? Items
) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var duplicates = (Items ?? [])
            .Where(l => l.ProductId != null)
            .GroupBy(l => l.ProductId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            yield return new ValidationResult(
                $"Each product may appear only once: {string.Join(", ", duplicates)}.",
                [nameof(Items)]
            );
        }
    }
}

public record AllocationItemRequestDto(
    [Required] Guid? ProductId,
    [Required] [Range(1, int.MaxValue)] int? Quantity
);

// Batches are suggested earliest expiry first (no date last), skipping expired ones; a batch's
// Available includes what OrderId already holds from it. ShortBy is what no batch could cover.
public record ItemAllocationSuggestionDto(
    Guid ProductId,
    string? SKU,
    string? Name,
    int Quantity,
    int Allocated,
    bool Shortage,
    int ShortBy,
    IReadOnlyList<BatchAllocationSuggestionDto> Batches
);

public record BatchAllocationSuggestionDto(
    Guid BatchId,
    string? Lot,
    DateTime? ExpirationDate,
    string? Location,
    int Available,
    int Quantity
);

// Units of a batch an order currently holds.
public record BatchOrderReadDto(Guid OrderId, Guid BatchId, int Quantity);
