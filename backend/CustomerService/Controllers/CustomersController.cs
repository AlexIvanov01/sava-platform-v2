using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CustomerService.AsyncDataServices;
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
        private readonly IMessageBusClient _messageBusClient;

        public CustomersController(ICustomerRepo repository, IMessageBusClient messageBusClient)
        {
            _repository = repository;
            _messageBusClient = messageBusClient;
        }

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<IEnumerable<CustomerReadDto>>> GetCustomers()
        {
            try
            {
                Log.Information("--> Getting all customers.........");

                var customers = await _repository.GetAllCustomersAsync();

                if(!customers.Any())
                {
                    Log.Warning("--> No customers found in database.");
                    return NotFound();
                }

                Log.Information("--> Fetched all customers from database.");

                return Ok(customers.Select(c => c.ToReadDto()));
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
                return StatusCode(500, "An internal server error occured.");
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

                return Ok(customer.ToReadDto());
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
                return StatusCode(500, "An internal server error occured.");
            }
        }

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status201Created)]
        public async Task<ActionResult<CustomerReadDto>> CreateCustomer(CustomerCreateDto customerCreateDto)
        {
            CustomerReadDto customerReadDto;
            Customer customerModel;

            try
            {
                Log.Information("--> Creating a product.............");
                customerModel = customerCreateDto.ToEntity();
                await _repository.CreateCustomerAsync(customerModel);

                Log.Information("--> Customer created: {Id}", customerModel.Id);

                customerReadDto = customerModel.ToReadDto();

            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
                return StatusCode(500, "An internal server error occured.");
            }

            //Send Async Message
            try
            {
                var customerPublishedDto = customerModel.ToPublishedDto("Customer_Published");
                await _messageBusClient.PublishNewCustomerAsync(customerPublishedDto);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error with publishing customer message: {Message}", ex.Message);
            }

            return CreatedAtRoute(nameof(GetCustomerById), new { Id = customerReadDto.Id }, customerReadDto);
        }

        [HttpPut("{id}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<ActionResult<CustomerReadDto>> UpdateCustomer(Guid id, CustomerUpdateDto customerUpdateDto)
        {
            Customer customerModel;

            try
            {
                Log.Information("--> Updating a customer with id {Id}....................", id);

                customerModel = customerUpdateDto.ToEntity(id);

                var nullCHeck = await _repository.UpdateCustomerAsync(customerModel);

                if (nullCHeck == null)
                {
                    Log.Warning("--> Customer with id {Id} not found for updating.", id);
                    return NotFound();
                }

                Log.Information("--> Customer with id {Id} updated", id);

            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
                return StatusCode(500, "An internal server error occured.");
            }

            //Send Async Message
            try
            {
                var customerPublishedDto = customerModel.ToPublishedDto("Customer_Updated");
                await _messageBusClient.PublishNewCustomerAsync(customerPublishedDto);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error with publishing customer message: {Message}", ex.Message);
            }

            return Ok(customerModel.ToReadDto());
        }

        [HttpDelete("{id}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> DeleteCustomer(Guid id)
        {
            try
            {
                Log.Information("--> Deleting a customer...........");

                var nullCheck = await _repository.DeleteCustomerAsync(id);


                if (nullCheck == null)
                {
                    Log.Warning("--> Customer with id {Id} not found for deleting.", id);
                    return NotFound();
                }

                Log.Information("--> Customer with id {Id} deleted", id);

            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
                return StatusCode(500, "An internal server error occured.");
            }

            //Send Async Message
            try
            {
                CustomerPublishedDto customerPublishedDto = new()
                {
                    ExternalId = id,
                    Event = "Customer_Deleted"
                };
                await _messageBusClient.PublishNewCustomerAsync(customerPublishedDto);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error with publishing customer message: {Message}", ex.Message);
            }
            return NoContent();
        }
    }
}
