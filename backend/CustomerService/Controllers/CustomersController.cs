using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using CustomerService.DataAccess;
using CustomerService.Dtos;
using CustomerService.Mappings;
using CustomerService.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace CustomerService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class CustomersController : ControllerBase
    {
        private readonly ICustomerRepo _repository;

        public CustomersController(ICustomerRepo repository)
        {
            _repository = repository;
        }

        // Filters combine with AND and match substrings; name also matches the company name.
        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<CustomerPageReadDto>> GetCustomers(
            Guid? cursor,
            [Range(1, 100)] int pageSize = 10,
            string? name = null,
            string? uic = null,
            string? vatNumber = null
        )
        {
            try
            {
                var filter = new CustomerFilter(name, uic, vatNumber);
                Log.Information(
                    "--> Getting {PageSize} customers after cursor {Cursor} with {@Filter}",
                    pageSize,
                    cursor,
                    filter
                );

                var customers = (
                    await _repository.GetCustomersAsync(filter, cursor, pageSize)
                ).ToList();

                if (customers.Count == 0)
                {
                    Log.Warning("--> No customers found after cursor {Cursor}.", cursor);
                    return NotFound();
                }

                var pages = (int)
                    Math.Ceiling(
                        await _repository.GetCustomersCountAsync(filter) / (double)pageSize
                    );

                return Ok(
                    new CustomerPageReadDto(
                        customers.Select(c => c.Customer.ToReadDto(c.HasOrders)).ToList(),
                        customers[^1].Customer.Id,
                        pages
                    )
                );
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
                return StatusCode(500, "An internal server error occurred.");
            }
        }

        [HttpGet("{id}", Name = "GetCustomerById")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<CustomerReadDto>> GetCustomerById(Guid id)
        {
            try
            {
                Log.Information("--> Getting a customer with id {Id}........", id);

                var customer = await _repository.GetCustomerAsync(id);

                if (customer == null)
                {
                    Log.Warning("--> Customer with id {Id} not found.", id);
                    return NotFound();
                }

                Log.Information("--> Fetched a customer with id {Id}.", id);

                return Ok(customer.ToReadDto(await _repository.HasOrdersAsync(id)));
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
                return StatusCode(500, "An internal server error occurred.");
            }
        }

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        public async Task<ActionResult<CustomerReadDto>> CreateCustomer(
            CustomerCreateDto customerCreateDto
        )
        {
            CustomerReadDto customerReadDto;
            Customer customerModel;

            try
            {
                Log.Information("--> Creating a product.............");
                customerModel = customerCreateDto.ToEntity();
                await _repository.CreateCustomerAsync(customerModel);

                Log.Information("--> Customer created: {Id}", customerModel.Id);

                customerReadDto = customerModel.ToReadDto(false);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
                return StatusCode(500, "An internal server error occurred.");
            }

            return CreatedAtRoute(
                nameof(GetCustomerById),
                new { Id = customerReadDto.Id },
                customerReadDto
            );
        }

        [HttpPut("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<CustomerReadDto>> UpdateCustomer(
            Guid id,
            CustomerUpdateDto customerUpdateDto
        )
        {
            Customer savedCustomer;

            try
            {
                Log.Information("--> Updating a customer with id {Id}....................", id);

                var updated = await _repository.UpdateCustomerAsync(customerUpdateDto.ToEntity(id));

                if (updated == null)
                {
                    Log.Warning("--> Customer with id {Id} not found for updating.", id);
                    return NotFound();
                }

                savedCustomer = updated;

                Log.Information("--> Customer with id {Id} updated", id);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
                return StatusCode(500, "An internal server error occurred.");
            }

            return Ok(savedCustomer.ToReadDto(await _repository.HasOrdersAsync(id)));
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> DeleteCustomer(Guid id)
        {
            try
            {
                Log.Information("--> Deleting a customer...........");

                var (status, _) = await _repository.DeleteCustomerAsync(id);

                if (status == DeleteCustomerStatus.NotFound)
                {
                    Log.Warning("--> Customer with id {Id} not found for deleting.", id);
                    return NotFound();
                }

                if (status == DeleteCustomerStatus.HasOrders)
                {
                    Log.Warning("--> Customer with id {Id} has orders; not deleted.", id);
                    return Problem(
                        title: "Customer has orders",
                        detail: $"Customer {id} is referenced by orders and can't be deleted.",
                        statusCode: StatusCodes.Status409Conflict
                    );
                }

                Log.Information("--> Customer with id {Id} deleted", id);
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
                return StatusCode(500, "An internal server error occurred.");
            }

            return NoContent();
        }
    }
}
