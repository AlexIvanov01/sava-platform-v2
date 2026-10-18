using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.Mappings;
using OrderService.Models;
using Serilog;

namespace OrderService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class InvoicesController : ControllerBase
    {
        private readonly IInvoiceRepo _repository;

        public InvoicesController(IInvoiceRepo repository)
        {
            _repository = repository;
        }

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<InvoicePageReadDto>> GetInvoices(
        int? cursor,
        [Range(1, 100)] int pageSize = 10)
        {
            try
            {
                Log.Information("--> Getting {PageSize} amount of invoices after cursor {Cursor}", pageSize, cursor);

                var invoiceItems = await _repository.GetAllInvociesAsync(cursor, pageSize);
 

                if (!invoiceItems.Any())
                {
                    Log.Warning("--> No invoices found in database after cursor {Cursor} or cursor not found.", cursor);
                    return NotFound();
                }

                Log.Information("--> Fetched {PageSize} amount of invoices from database after cursor {Cursor}.", pageSize, cursor);

                var pageCount = Math.Ceiling(await _repository.GetInvoiceCountAsync() / (float)pageSize);

                List<OrderFullReadDto> dtos = invoiceItems.Select(i => i.ToFullReadDto()).ToList();

#pragma warning disable CS8602 // Dereference of a possibly null reference.
                InvoicePageReadDto invoicePageReadDto = new()
                {
                    OrderReadDtos = dtos,
                    Cursor = dtos.Select(o => o.Invoice).Last().Id,
                    Pages = (int)pageCount
                };
#pragma warning restore CS8602 // Dereference of a possibly null reference.

                return Ok(invoicePageReadDto);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message); ;
                return StatusCode(500, "An internal server error occured.");
            }
        }

        [HttpGet("{id}", Name = "GetInvoiceById")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<OrderFullReadDto>> GetInvoiceById(int id)
        {
            try
            {
                Log.Information("--> Getting an invoice with id {Id}........", id);

                var invoice = await _repository.GetInvocieByIdAsync(id);

                if (invoice == null)
                {
                    Log.Warning("Invoice with id {Id} not found.", id);
                    return NotFound();
                }

                Log.Information("--> Fetched an invoice with id {Id}.", id);

                var dto = invoice.ToFullReadDto();
                    return Ok(dto);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
                return StatusCode(500, "An internal server error occured.");
            }
        }

        [HttpPost("{orderId}")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<InvoiceReadDto>> CreateInvoice(InvoiceCreateDto invoiceCreateDto, Guid orderId)
        {
            try
            {
                Log.Information("--> Creating an invoice............");

                var invoiceModel = invoiceCreateDto.ToEntity();

                var nullCheck = await _repository.CreateInvocieAsync(invoiceModel, orderId);

                if (nullCheck == null)
                {
                    return NotFound();
                }

                Log.Information("--> Created invoice for order: {Id}", orderId);

                var invoiceReadDto = invoiceModel.ToReadDto();

                return CreatedAtRoute(nameof(GetInvoiceById), new { Id = invoiceReadDto.Id }, invoiceReadDto);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message); ;
                return StatusCode(500, "An internal server error occured.");
            }
        }

        [HttpPut("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<InvoiceReadDto>> UpdateInvoice(int id, InvoiceUpdateDto invoiceUpdateDto)
        {
            try
            {
                Log.Information("--> Updating invoice.............");

                var invoiceModel = invoiceUpdateDto.ToEntity(id);

                var nullCheck = await _repository.UpdateInvoiceAsync(invoiceModel);

                if (nullCheck == null)
                {
                    Log.Warning("Invoice with id {Id} not found for updating.", id);
                    return NotFound();
                }

                Log.Information("--> Invoice uptated: {Id}", id);

                return Ok(invoiceModel.ToReadDto());
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
                return StatusCode(500, "An internal server error occured.");
            }
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult> DeleteInvoice(int id)
        {
            try
            {
                Log.Information("--> Deleting a batch...........");

                var nullCHeck = await _repository.DeleteInvoiceAsync(id);
                if (nullCHeck == null)
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
                return StatusCode(500, "An internal server error occured.");
            }
        }
    }
}
