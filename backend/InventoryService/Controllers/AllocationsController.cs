using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InventoryService.Allocation;
using InventoryService.DataAccess;
using InventoryService.Dtos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace InventoryService.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AllocationsController(
    IProductRepo productRepo,
    IStockRepo stockRepo,
    TimeProvider timeProvider
) : ControllerBase
{
    // Read-only: suggests batches per item and reports shortages. Saving an order is up to
    // OrderService, which accepts any batches and quantities.
    [HttpPost("suggest")]
    [ProducesResponseType<IEnumerable<ItemAllocationSuggestionDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<ItemAllocationSuggestionDto>>> Suggest(
        AllocationSuggestRequestDto request
    )
    {
        try
        {
            var productIds = request.Items!.Select(l => l.ProductId!.Value).ToList();
            var products = (await productRepo.GetProductsByIdsAsync(productIds)).ToDictionary(p =>
                p.Id
            );

            var missing = productIds.Where(id => !products.ContainsKey(id)).ToList();
            if (missing.Count > 0)
            {
                return Problem(
                    $"Unknown products: {string.Join(", ", missing)}.",
                    statusCode: StatusCodes.Status400BadRequest
                );
            }

            var heldByOrder = request.OrderId is { } orderId
                ? await stockRepo.GetOrderHoldingsAsync(orderId)
                : new Dictionary<Guid, int>();
            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

            return Ok(
                request
                    .Items!.Select(l =>
                        FefoAllocator.Suggest(
                            products[l.ProductId!.Value],
                            l.Quantity!.Value,
                            heldByOrder,
                            today
                        )
                    )
                    .ToList()
            );
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }
}
