using System.Text.Json.Serialization;

namespace InventoryService.Models;

// Stored and serialized by name, so adding a unit needs no data migration.
[JsonConverter(typeof(JsonStringEnumConverter<UnitOfMeasure>))]
public enum UnitOfMeasure
{
    Piece,
    Kilogram,
    Liter,
    Meter,
    Box,
    Pack,
}
