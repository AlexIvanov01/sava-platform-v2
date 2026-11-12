using System;

namespace InventoryService.Models;

// Units of a batch an order currently holds, i.e. its net Sale/SaleReversal quantity in the ledger.
public record OrderHolding(Guid OrderId, Guid BatchId, int Quantity);
