using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InventoryService.DataAccess;
using InventoryService.Dtos;
using InventoryService.Mappings;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace InventoryService.Controllers;

// The stock ledger. Routes hang off the batch or product they belong to.
[Route("api")]
[ApiController]
public class StockMovementsController(IStockRepo repository) : ControllerBase
{
    // Manual stock change (damage, shrinkage, expiry write-off, count correction, ...).
    [HttpPost("Batches/{id}/adjustments")]
    [ProducesResponseType<StockMovementReadDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<StockMovementReadDto>> AdjustStock(
        Guid id,
        StockAdjustmentCreateDto dto
    )
    {
        try
        {
            var movement = await repository.AdjustAsync(
                id,
                dto.Quantity!.Value,
                dto.Reason!.Value,
                dto.Note
            );

            if (movement == null)
            {
                Log.Warning("--> Batch with id {Id} not found for adjusting.", id);
                return NotFound();
            }

            Log.Information(
                "--> Adjusted batch {Id} by {Quantity} ({Reason}).",
                id,
                dto.Quantity,
                dto.Reason
            );
            return Created($"/api/Batches/{id}/movements", movement.ToReadDto());
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    // Newest first; from/to filter on when the movement was recorded.
    [HttpGet("Batches/{id}/movements")]
    [ProducesResponseType<IEnumerable<StockMovementReadDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<StockMovementReadDto>>> GetBatchMovements(
        Guid id,
        DateTime? from = null,
        DateTime? to = null
    )
    {
        try
        {
            var movements = await repository.GetBatchMovementsAsync(id, from, to);

            if (movements == null)
            {
                Log.Warning("--> Batch with id {Id} not found.", id);
                return NotFound();
            }

            return Ok(movements.Select(m => m.ToReadDto()));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    // Movements of all the product's batches, newest first.
    [HttpGet("Products/{id}/movements")]
    [ProducesResponseType<IEnumerable<StockMovementReadDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<StockMovementReadDto>>> GetProductMovements(
        Guid id,
        DateTime? from = null,
        DateTime? to = null
    )
    {
        try
        {
            var movements = await repository.GetProductMovementsAsync(id, from, to);

            if (movements == null)
            {
                Log.Warning("--> Product with id {Id} not found.", id);
                return NotFound();
            }

            return Ok(movements.Select(m => m.ToReadDto()));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }
}
