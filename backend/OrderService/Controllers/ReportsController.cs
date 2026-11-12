using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.Reporting;
using Serilog;

namespace OrderService.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ReportsController(IOrderRepo repository) : ControllerBase
{
    // Completed orders whose invoice date falls in [from, to] (inclusive days), grouped by
    // customer (default), product or month, with a totals row. Cancelled orders don't count.
    [HttpGet("sales")]
    [ProducesResponseType<SalesReportDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SalesReportDto>> GetSales(
        DateOnly? from,
        DateOnly? to,
        Guid? customerId,
        Guid? productId,
        SalesGrouping groupBy = SalesGrouping.Customer
    )
    {
        try
        {
            if (from > to)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid period",
                    detail: "from must not be after to."
                );
            }

            var orders = await repository.GetCompletedOrdersAsync(from, to, customerId);
            return Ok(SalesReport.Build(orders, groupBy, from, to, productId));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }
}
