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
using OrderService.Ordering;
using OrderService.SyncDataServices.Grpc;
using Serilog;

namespace OrderService.Controllers;

[Route("api/[controller]")]
[ApiController]
public class OrdersController : ControllerBase
{
    private readonly IOrderRepo _repository;
    private readonly IMessageBusClient _messageBusClient;
    private readonly IInventoryDataClient _inventory;
    private readonly ICustomerDataClient _customers;

    public OrdersController(
        IOrderRepo orderRepo,
        IMessageBusClient messageBusClient,
        IInventoryDataClient inventory,
        ICustomerDataClient customers
    )
    {
        _repository = orderRepo;
        _messageBusClient = messageBusClient;
        _inventory = inventory;
        _customers = customers;
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
                Log.Warning("--> No orders were found after {Cursor} or cursor not found.", cursor);
                return NotFound();
            }

            var pageCount = Math.Ceiling(await _repository.GetOrderCountAsync() / (float)pageSize);

            var dtos = orderPageItems.Select(o => o.ToReadDto()).ToList();

            return Ok(
                new OrderPageReadDto
                {
                    Orders = dtos,
                    Cursor = dtos[^1].Id,
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

    [HttpGet("{id}", Name = "GetOrderById")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderReadDto>> GetOrderById(Guid id)
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

            return Ok(orderModel.ToReadDto());
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Internal server error");
            return StatusCode(500, "An internal server error occurred.");
        }
    }

    // Snapshots the customer and the products and batches from CustomerService and
    // InventoryService; 503 when either doesn't answer. Stock isn't checked.
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderReadDto>> CreateOrder(OrderCreateDto orderCreateDto)
    {
        Order order;

        try
        {
            Log.Information("--> Creating an order.............");
            order = orderCreateDto.ToEntity();

            var customer = await _customers.GetCustomerAsync(orderCreateDto.CustomerId!.Value);
            if (customer == null)
            {
                return UnknownCustomer(orderCreateDto.CustomerId.Value);
            }
            order.Customer = customer;

            var problem = await ApplyItemsAsync(order, orderCreateDto.Items!);
            if (problem != null)
            {
                return problem;
            }

            await _repository.CreateOrderAsync(order);

            Log.Information("--> Customer order created: {Id}", order.Id);
        }
        catch (UpstreamUnavailableException ex)
        {
            return Unavailable(ex);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }

        await PublishAsync(order, "Order_Published");

        var dto = order.ToReadDto();
        return CreatedAtRoute(nameof(GetOrderById), new { dto.Id }, dto);
    }

    // Partial update; see OrderUpdateDto for how Items is applied. A new CustomerId takes a fresh
    // customer snapshot.
    [HttpPut("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OrderReadDto>> UpdateOrder(
        Guid id,
        OrderUpdateDto orderUpdateDto
    )
    {
        Order order;
        try
        {
            Log.Information("--> Updating an order with id {Id}....................", id);

            var found = await _repository.GetOrderForUpdateAsync(id);
            if (found == null)
            {
                Log.Warning("--> Order with id {Id} not found for updating.", id);
                return NotFound();
            }
            order = found;

            if (orderUpdateDto.CustomerId is { } customerId && customerId != order.CustomerId)
            {
                var customer = await _customers.GetCustomerAsync(customerId);
                if (customer == null)
                {
                    return UnknownCustomer(customerId);
                }
                order.CustomerId = customerId;
                order.Customer = customer;
            }

            if (orderUpdateDto.Items != null)
            {
                var problem = await ApplyItemsAsync(order, orderUpdateDto.Items);
                if (problem != null)
                {
                    return problem;
                }
                // Items belong to the order, so changing them counts as modifying the order
                // (AuditStamper only stamps entities the change tracker sees as Modified).
                order.UpdatedAt = DateTime.UtcNow;
            }

            order.OrderDate = orderUpdateDto.OrderDate ?? order.OrderDate;
            order.ShippedDate = orderUpdateDto.ShippedDate ?? order.ShippedDate;
            order.ShippingAddress = orderUpdateDto.ShippingAddress ?? order.ShippingAddress;
            order.OrderStatus = orderUpdateDto.OrderStatus ?? order.OrderStatus;

            await _repository.SaveChangesAsync();

            Log.Information("--> Order with id {Id} updated", id);
        }
        catch (UpstreamUnavailableException ex)
        {
            return Unavailable(ex);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }

        // Always the full current state: subscribers apply only what changed (Inventory moves
        // stock for changed batches, CustomerService follows a change of customer).
        await PublishAsync(order, "Order_Published");

        return Ok(order.ToReadDto());
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteOrder(Guid id)
    {
        Order deletedOrder;
        try
        {
            Log.Information("--> Deleting an order...........");

            var (status, order) = await _repository.DeleteOrderAsync(id);

            if (status == DeleteOrderStatus.NotFound)
            {
                Log.Warning("--> Order with id {Id} not found for deleting.", id);
                return NotFound();
            }

            if (status == DeleteOrderStatus.HasInvoice)
            {
                Log.Warning("--> Order with id {Id} has an invoice and was not deleted.", id);
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Order has an invoice",
                    detail: $"Order {id} has an invoice. Delete the invoice before deleting the order."
                );
            }

            Log.Information("--> Order with id {Id} deleted", id);

            deletedOrder = order!;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }

        await PublishAsync(deletedOrder, "Order_Deleted");

        return NoContent();
    }

    // Snapshots what is new in the request and applies it; a problem result when it is invalid.
    private async Task<ObjectResult?> ApplyItemsAsync(
        Order order,
        IReadOnlyList<OrderItemWriteDto> items
    )
    {
        var (productIds, batchIds) = OrderItems.SnapshotIds(order.Items, items);
        var snapshot =
            productIds.Count == 0 && batchIds.Count == 0
                ? new CatalogSnapshot(
                    new Dictionary<Guid, ProductSnapshot>(),
                    new Dictionary<Guid, BatchSnapshot>()
                )
                : await _inventory.GetOrderSnapshotAsync(productIds, batchIds);

        var errors = OrderItems.Apply(order, items, snapshot);
        if (errors.Count == 0)
        {
            return null;
        }

        Log.Warning("--> Invalid order items: {Errors}", errors);
        return Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Invalid order items",
            detail: string.Join(" ", errors)
        );
    }

    private ObjectResult UnknownCustomer(Guid customerId)
    {
        Log.Warning("--> Customer {Id} not found.", customerId);
        return Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Unknown customer",
            detail: $"Customer {customerId} does not exist."
        );
    }

    private ObjectResult Unavailable(UpstreamUnavailableException ex)
    {
        Log.Warning("--> {Service} is unavailable; the order was not saved.", ex.Service);
        return Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            title: $"{ex.Service} unavailable",
            detail: $"{ex.Service} did not respond, so the order was not saved. Try again."
        );
    }

    // After the database change; a bus failure doesn't fail the request.
    private async Task PublishAsync(Order order, string eventName)
    {
        try
        {
            await _messageBusClient.PublishNewOrderAsync(
                new OrderPublishedEvent
                {
                    OrderId = order.Id,
                    CustomerId = order.CustomerId.GetValueOrDefault(),
                    Items = order.Items.ToPublishedDtos(),
                    Event = eventName,
                }
            );
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error with publishing order message: {Message}", ex.Message);
        }
    }
}
