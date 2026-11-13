using System;

namespace InventoryService.Dtos;

// The fields every order event has; read first to file the message in the inbox.
public class GenericEventDto
{
    public string Event { get; set; } = string.Empty;
    public Guid? MessageId { get; set; }
    public Guid? OrderId { get; set; }
}
