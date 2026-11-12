using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.Invoicing;
using OrderService.Mappings;
using OrderService.Models;
using Serilog;

namespace OrderService.Controllers;

// Invoices are created by completing their order (POST /api/Orders/{id}/status).
[Route("api/[controller]")]
[ApiController]
public class InvoicesController(IInvoiceRepo repository, TimeProvider timeProvider) : ControllerBase
{
    private DateOnly Today => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    // Filters combine with AND and apply to Pages too; see InvoiceFilter.
    [HttpGet]
    [ProducesResponseType<InvoicePageReadDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoicePageReadDto>> GetInvoices(
        Guid? cursor,
        DateOnly? from,
        DateOnly? to,
        Guid? customerId,
        string? number,
        PaymentStatus? paymentStatus,
        bool? overdue,
        [Range(1, 100)] int pageSize = 10
    )
    {
        try
        {
            var filter = new InvoiceFilter(from, to, customerId, number, paymentStatus, overdue);
            var today = Today;
            Log.Information(
                "--> Getting {PageSize} invoices after cursor {Cursor} matching {@Filter}",
                pageSize,
                cursor,
                filter
            );

            var invoices = (
                await repository.GetInvoicesAsync(filter, today, cursor, pageSize)
            ).ToList();

            if (invoices.Count == 0)
            {
                Log.Warning("--> No invoices found after cursor {Cursor}.", cursor);
                return NotFound();
            }

            var pageCount = Math.Ceiling(
                await repository.GetInvoiceCountAsync(filter, today) / (float)pageSize
            );

            return Ok(
                new InvoicePageReadDto
                {
                    Invoices = invoices.Select(i => i.ToReadDto(today)).ToList(),
                    Cursor = invoices[^1].Id,
                    Pages = (int)pageCount,
                }
            );
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    // Whether a number is already used, and the number the next invoice would get. Numbers may
    // repeat; this is only for warnings.
    [HttpGet("numbering")]
    [ProducesResponseType<InvoiceNumberCheckDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<InvoiceNumberCheckDto>> CheckNumber(
        [Required]
        [RegularExpression(@"^\d{10}$", ErrorMessage = "Number must be 10 digits.")]
            string number
    )
    {
        try
        {
            return Ok(
                new InvoiceNumberCheckDto(
                    number,
                    await repository.NumberExistsAsync(number),
                    await repository.NextNumberAsync()
                )
            );
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    [HttpGet("{id:guid}", Name = "GetInvoiceById")]
    [ProducesResponseType<InvoiceReadDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceReadDto>> GetInvoiceById(Guid id)
    {
        try
        {
            var invoice = await repository.GetInvoiceByIdAsync(id);

            if (invoice == null)
            {
                Log.Warning("Invoice with id {Id} not found.", id);
                return NotFound();
            }

            return Ok(invoice.ToReadDto(Today));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    // Partial update of the number, dates, payment fields and VAT exemption reason.
    [HttpPut("{id:guid}")]
    [ProducesResponseType<InvoiceReadDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceReadDto>> UpdateInvoice(Guid id, InvoiceUpdateDto dto)
    {
        try
        {
            var invoice = await repository.GetInvoiceForUpdateAsync(id);

            if (invoice == null)
            {
                Log.Warning("Invoice with id {Id} not found for updating.", id);
                return NotFound();
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            var invalid = InvoiceChanges.Apply(invoice, dto, now);
            if (invalid != null)
            {
                return Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid invoice update",
                    detail: invalid
                );
            }

            await repository.SaveChangesAsync();
            Log.Information("--> Invoice updated: {Id}", id);

            return Ok(invoice.ToReadDto(DateOnly.FromDateTime(now)));
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    // A Completed order goes back to Pending; a Cancelled one stays Cancelled.
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteInvoice(Guid id)
    {
        try
        {
            var deleted = await repository.DeleteInvoiceAsync(id);
            if (deleted == null)
            {
                Log.Warning("Invoice with id {Id} not found for deleting.", id);
                return NotFound();
            }

            Log.Information("--> Invoice with id {Id} deleted", id);

            return NoContent();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }
    }
}
