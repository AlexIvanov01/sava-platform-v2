using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using InventoryService.DataAccess;
using InventoryService.Dtos;
using InventoryService.Reports;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace InventoryService.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ReportsController(IProductRepo repository, TimeProvider timeProvider) : ControllerBase
{
    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    // Products whose available stock is at or below their reorder level.
    [HttpGet("low-stock")]
    [ProducesResponseType<List<LowStockItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<List<LowStockItemDto>>> GetLowStock()
    {
        try
        {
            var products = await repository.GetProductsWithBatchesAsync();
            return Ok(StockReports.LowStock(products, Today));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    // Batches with stock expiring within `days` (0 = today); includeExpired adds already
    // expired batches that still hold stock, e.g. to write them off.
    [HttpGet("expiring")]
    [ProducesResponseType<List<ExpiringBatchDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<ExpiringBatchDto>>> GetExpiring(
        [Range(0, 365)] int days = 30,
        bool includeExpired = false
    )
    {
        try
        {
            var today = Today;
            var batches = await repository.GetBatchesInStockExpiringByAsync(
                today.AddDays(days).ToDateTime(TimeOnly.MinValue)
            );
            return Ok(StockReports.Expiring(batches, today, days, includeExpired));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    // Value of the stock on hand at purchase price, per product and in total.
    [HttpGet("stock-valuation")]
    [ProducesResponseType<StockValuationDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StockValuationDto>> GetStockValuation(Guid? categoryId = null)
    {
        try
        {
            var products = await repository.GetProductsWithBatchesAsync(categoryId);
            return Ok(StockReports.Valuation(products, Today));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }
}
