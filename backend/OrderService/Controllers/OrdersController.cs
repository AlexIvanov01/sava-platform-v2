using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OrderService.AsyncDataServices;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.Mappings;
using OrderService.Models;
using Serilog;

namespace OrderService.Controllers;

[Route("api/[controller]")]
[ApiController]
public class OrdersController : ControllerBase
{
    private readonly IOrderRepo _repository;
    private readonly IMessageBusClient _messageBusClient;

    public OrdersController(IOrderRepo orderRepo, IMessageBusClient messageBusClient)
    {
        _repository = orderRepo;
        _messageBusClient = messageBusClient;
    }

    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderPageReadDto>> GetAllOrdersAsync(
        Guid? cursor,
        Guid? customerId,
        [Range(1, 50)] int pageSize = 10
    )
    {
        try
        {
            IEnumerable<Order> orderPageItems;

            if (customerId == null)
            {
                Log.Information(
                    "--> Getting {PageSize} amount of orders after cursor {Cursor}",
                    pageSize,
                    cursor
                );
                orderPageItems = await _repository.GetAllOrdersAsync(cursor, pageSize);
            }
            else
            {
                Log.Information(
                    "--> Getting {PageSize} amount of orders made by customer {Customer} after cursor {Cursor}",
                    pageSize,
                    customerId,
                    cursor
                );

                orderPageItems = await _repository.GetAllOrdersByCustomerIdAsync(
                    customerId,
                    cursor,
                    pageSize
                );
            }

            if (!orderPageItems.Any())
            {
                Log.Warning(
                    "--> No orderds were found after {Cursor} or cursor not found.",
                    cursor
                );
                return NotFound();
            }

            var pageCount = Math.Ceiling(await _repository.GetOrderCountAsync() / (float)pageSize);

            List<OrderFullReadDto> dtos = orderPageItems.Select(o => o.ToFullReadDto()).ToList();

            var pageDto = new OrderPageReadDto
            {
                OrderFullReadDtos = dtos,
                Cursor = dtos[dtos.Count - 1].Id,
                Pages = (int)pageCount,
            };

            return Ok(pageDto);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occured.");
        }
    }

    [HttpGet("{id}", Name = "GetOrderById")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderFullReadDto>> GetOrderById(Guid id)
    {
        try
        {
            Log.Information("--> Getting an order with id {Id}........", id);

            var orderModel = await _repository.GetOrderByIdAsync(id);

            if (orderModel == null)
            {
                Log.Warning("Order with id {Id} not found.", id);
                return NotFound();
            }

            Log.Information("--> Fetched an order with id {Id}.", id);

            var orderFullReadDto = orderModel.ToFullReadDto();

            return Ok(orderFullReadDto);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Internal server error");
            return StatusCode(500, "An internal server error occured.");
        }
    }

    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderFullReadDto>> CreateOrder(OrderCreateDto orderCreateDto)
    {
        Order orderModel;

        try
        {
            Log.Information("--> Creating an order.............");
            orderModel = orderCreateDto.ToEntity();
            var validOrder = await _repository.CreateOrderAsync(orderModel);

            if (validOrder == null)
            {
                return NotFound();
            }

            Log.Information("--> Customer order created: {Id}", orderModel.Id);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occured.");
        }

        //Send Async Message
        try
        {
            OrderPublishedEvent orderPublishedEvent = new()
            {
                OrderItems = orderModel.OrderItems.ToPublishedDtos(),
                Event = "Order_Published",
            };

            await _messageBusClient.PublishNewOrderAsync(orderPublishedEvent);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error with publishing order message: {Message}", ex.Message);
        }

        var orderFullReadDto = orderModel.ToFullReadDto();

        return CreatedAtRoute(nameof(GetOrderById), new { orderFullReadDto.Id }, orderFullReadDto);
    }

    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderReadDto>> UpdateOrder(
        Guid id,
        OrderUpdateDto orderUpdateDto
    )
    {
        Order updatedOrder;
        List<OrderItem> previousItems;
        try
        {
            Log.Information("--> Updating a product with id {Id}....................", id);

            var result = await _repository.UpdateOrderAsync(orderUpdateDto.ToEntity(id));

            if (result == null)
            {
                return NotFound();
            }
            (updatedOrder, previousItems) = result.Value;

            Log.Information("--> Order with id {Id} updated", id);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occured.");
        }

        //Send Async Message
        try
        {
            if (orderUpdateDto.OrderItems != null && orderUpdateDto.OrderItems.Count != 0)
            {
                OrderPublishedEvent orderDeletedEvent = new()
                {
                    OrderItems = previousItems.ToPublishedDtos(),
                    Event = "Order_Deleted",
                };

                await _messageBusClient.PublishNewOrderAsync(orderDeletedEvent);

                OrderPublishedEvent orderPublishedEvent = new()
                {
                    OrderItems = orderUpdateDto.OrderItems.ToPublishedDtos(),
                    Event = "Order_Published",
                };

                await _messageBusClient.PublishNewOrderAsync(orderPublishedEvent);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error with publishing order message: {Message}", ex.Message);
        }

        return Ok(updatedOrder.ToReadDto());
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteOrder(Guid id)
    {
        Order preDeleteOrderModel;
        try
        {
            Log.Information("--> Deleting an order...........");

            var nullCheck = await _repository.DeleteOrderAsync(id);

            if (nullCheck == null)
            {
                Log.Warning("--> Order with id {Id} not found for deleting.", id);
                return NotFound();
            }

            Log.Information("--> Order with id {Id} deleted", id);

            preDeleteOrderModel = nullCheck;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occured.");
        }

        //Send async Message
        try
        {
            OrderPublishedEvent orderDeletedEvent = new()
            {
                OrderItems = preDeleteOrderModel.OrderItems.ToPublishedDtos(),
                Event = "Order_Deleted",
            };

            await _messageBusClient.PublishNewOrderAsync(orderDeletedEvent);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error with publishing order message: {Message}", ex.Message);
        }
        return NoContent();
    }
}
