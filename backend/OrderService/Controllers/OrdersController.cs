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
    private readonly TimeProvider _timeProvider;

    public OrdersController(
        IOrderRepo orderRepo,
        IMessageBusClient messageBusClient,
        IInventoryDataClient inventory,
        ICustomerDataClient customers,
        TimeProvider timeProvider
    )
    {
        _timeProvider = timeProvider;
        _repository = orderRepo;
        _messageBusClient = messageBusClient;
        _inventory = inventory;
        _customers = customers;
    }

    // Filters combine with AND and apply to Pages too; from/to are inclusive days of OrderDate.
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderPageReadDto>> GetAllOrdersAsync(
        Guid? cursor,
        Guid? customerId,
        OrderStatus? status,
        DateOnly? from,
        DateOnly? to,
        [Range(1, 50)] int pageSize = 10
    )
    {
        try
        {
            var filter = new OrderFilter(status, customerId, from, to);
            Log.Information(
                "--> Getting {PageSize} orders after cursor {Cursor} matching {@Filter}",
                pageSize,
                cursor,
                filter
            );

            var orderPageItems = await _repository.GetOrdersAsync(filter, cursor, pageSize);

            if (!orderPageItems.Any())
            {
                Log.Warning("--> No orders were found after {Cursor} or cursor not found.", cursor);
                return NotFound();
            }

            var pageCount = Math.Ceiling(
                await _repository.GetOrderCountAsync(filter) / (float)pageSize
            );

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
    [ProducesResponseType(StatusCodes.Status409Conflict)]
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

            if (!order.IsEditable())
            {
                Log.Warning("--> Order {Id} is {Status} and can't be edited.", id, order.Status);
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Order is locked",
                    detail: $"Order {id} is {order.Status}; only InProcess and Pending orders can be edited."
                );
            }

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
            order.ShippingAddress = orderUpdateDto.ShippingAddress ?? order.ShippingAddress;

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

    // See Ordering/OrderLifecycle for the allowed changes; others are 409. Completing creates the
    // invoice (gross total); cancelling releases the stock (Order_Deleted) and keeps any invoice;
    // reopening a cancelled order takes the stock again (Order_Published). Setting the current
    // status again changes nothing.
    [HttpPost("{id}/status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OrderReadDto>> ChangeStatus(Guid id, OrderStatusChangeDto dto)
    {
        Order order;
        OrderStatus previous;
        try
        {
            var found = await _repository.GetOrderForUpdateAsync(id);
            if (found == null)
            {
                Log.Warning("--> Order with id {Id} not found for a status change.", id);
                return NotFound();
            }
            order = found;
            previous = order.Status;
            var target = dto.Status!.Value;

            if (previous == target)
            {
                return Ok(order.ToReadDto());
            }

            var refusal = OrderLifecycle.CheckTransition(order, target);
            if (refusal != null)
            {
                Log.Warning(
                    "--> Order {Id} can't go from {From} to {To}: {Reason}",
                    id,
                    previous,
                    target,
                    refusal
                );
                return Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Status change not allowed",
                    detail: refusal
                );
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            switch (target)
            {
                case OrderStatus.Completed:
                    order.CompletedDate = now;
                    order.Invoice = new Invoice
                    {
                        Order = order,
                        OrderId = order.Id,
                        InvoiceDate = now,
                        InvoiceStatus = "Unpaid",
                        TotalAmount = order.Items.Gross(),
                    };
                    break;
                case OrderStatus.Cancelled:
                    order.CancelledDate = now;
                    break;
                case OrderStatus.Pending when previous == OrderStatus.Cancelled:
                    order.CancelledDate = null;
                    break;
            }
            order.Status = target;

            await _repository.SaveChangesAsync();
            Log.Information("--> Order {Id} went from {From} to {To}.", id, previous, target);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "--> Internal server error: {Message}", ex.Message);
            return StatusCode(500, "An internal server error occurred.");
        }

        if (order.Status == OrderStatus.Cancelled)
        {
            await PublishAsync(order, "Order_Deleted");
        }
        else if (previous == OrderStatus.Cancelled)
        {
            await PublishAsync(order, "Order_Published");
        }

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
