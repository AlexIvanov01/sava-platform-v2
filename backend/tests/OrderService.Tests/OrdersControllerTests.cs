using Microsoft.AspNetCore.Mvc;
using NSubstitute.ExceptionExtensions;
using OrderService.AsyncDataServices;
using OrderService.Controllers;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.Models;

namespace OrderService.Tests;

public class OrdersControllerTests
{
    private readonly IOrderRepo _repo = Substitute.For<IOrderRepo>();
    private readonly IMessageBusClient _bus = Substitute.For<IMessageBusClient>();
    private readonly OrdersController _controller;
    private readonly List<OrderPublishedEvent> _published = [];

    public OrdersControllerTests()
    {
        _controller = new OrdersController(_repo, _bus);
        _bus.PublishNewOrderAsync(Arg.Do<OrderPublishedEvent>(_published.Add));
    }

    [Fact]
    public async Task GetAllOrders_NoCustomerFilter_UsesAllOrdersAndBuildsPage()
    {
        var customer = OrderBuilder.Customer();
        var orders = new[] { OrderBuilder.Order(customer), OrderBuilder.Order(customer) };
        _repo.GetAllOrdersAsync(null, 2).Returns(orders);
        _repo.GetOrderCountAsync().Returns(3);

        var result = await _controller.GetAllOrdersAsync(null, null, 2);

        var page = result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderPageReadDto>();
        page.OrderFullReadDtos.Count().ShouldBe(2);
        page.Cursor.ShouldBe(orders[1].Id);
        page.Pages.ShouldBe(2);
        await _repo
            .DidNotReceiveWithAnyArgs()
            .GetAllOrdersByCustomerIdAsync(default, default, default);
    }

    [Fact]
    public async Task GetAllOrders_WithCustomerFilter_UsesCustomerQuery()
    {
        var customer = OrderBuilder.Customer();
        var cursor = Guid.NewGuid();
        _repo
            .GetAllOrdersByCustomerIdAsync(customer.ExternalId, cursor, 10)
            .Returns([OrderBuilder.Order(customer)]);

        var result = await _controller.GetAllOrdersAsync(cursor, customer.ExternalId);

        result.Result.ShouldBeOfType<OkObjectResult>();
        await _repo.DidNotReceiveWithAnyArgs().GetAllOrdersAsync(default, default);
    }

    [Fact]
    public async Task GetAllOrders_Empty_ReturnsNotFound()
    {
        _repo.GetAllOrdersAsync(null, 10).Returns([]);

        (await _controller.GetAllOrdersAsync(null, null)).Result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetAllOrders_RepoThrows_Returns500()
    {
        _repo
            .GetAllOrdersAsync(Arg.Any<Guid?>(), Arg.Any<int>())
            .ThrowsAsync(new InvalidOperationException());

        (await _controller.GetAllOrdersAsync(null, null))
            .Result.ShouldBeOfType<ObjectResult>()
            .StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task GetOrderById_MissingAndExisting()
    {
        var order = OrderBuilder.Order(OrderBuilder.Customer());
        _repo.GetOrderByIdAsync(order.Id).Returns(order);

        (await _controller.GetOrderById(Guid.NewGuid())).Result.ShouldBeOfType<NotFoundResult>();
        (await _controller.GetOrderById(order.Id))
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderFullReadDto>()
            .Id.ShouldBe(order.Id);
    }

    [Fact]
    public async Task CreateOrder_Invalid_ReturnsNotFoundAndDoesNotPublish()
    {
        var result = await _controller.CreateOrder(
            OrderBuilder.CreateDto(Guid.NewGuid(), (Guid.NewGuid(), 1))
        );

        result.Result.ShouldBeOfType<NotFoundResult>();
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateOrder_Valid_ReturnsCreatedAndPublishesOrderPublished()
    {
        var itemId = Guid.NewGuid();
        _repo.CreateOrderAsync(Arg.Any<Order>()).Returns(ci => ci.Arg<Order>());

        var result = await _controller.CreateOrder(
            OrderBuilder.CreateDto(Guid.NewGuid(), (itemId, 3))
        );

        result
            .Result.ShouldBeOfType<CreatedAtRouteResult>()
            .RouteName.ShouldBe(nameof(OrdersController.GetOrderById));
        var published = _published.ShouldHaveSingleItem();
        published.Event.ShouldBe("Order_Published");
        published.OrderItems.ShouldBe([new OrderItemPublishedDto(itemId, 3)]);
    }

    [Fact]
    public async Task CreateOrder_RepoThrows_Returns500()
    {
        _repo.CreateOrderAsync(Arg.Any<Order>()).ThrowsAsync(new InvalidOperationException());

        var result = await _controller.CreateOrder(
            OrderBuilder.CreateDto(Guid.NewGuid(), (Guid.NewGuid(), 1))
        );

        result.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task UpdateOrder_Missing_ReturnsNotFound()
    {
        (
            await _controller.UpdateOrder(Guid.NewGuid(), OrderBuilder.UpdateDto("x"))
        ).Result.ShouldBeOfType<NotFoundResult>();
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateOrder_WithItems_PublishesDeletedPreviousThenPublishedNew()
    {
        var oldItem = Guid.NewGuid();
        var newItem = Guid.NewGuid();
        var order = OrderBuilder.Order(OrderBuilder.Customer());
        List<OrderItem> previous = [new() { ItemId = oldItem, OrderItemQuantity = 2 }];
        _repo.UpdateOrderAsync(Arg.Any<Order>()).Returns((order, previous));

        var result = await _controller.UpdateOrder(
            order.Id,
            OrderBuilder.UpdateDto(lines: (newItem, 5))
        );

        result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderReadDto>()
            .Id.ShouldBe(order.Id);
        _published.Select(p => p.Event).ShouldBe(["Order_Deleted", "Order_Published"]);
        _published[0].OrderItems.ShouldBe([new OrderItemPublishedDto(oldItem, 2)]);
        _published[1].OrderItems.ShouldBe([new OrderItemPublishedDto(newItem, 5)]);
    }

    [Fact]
    public async Task UpdateOrder_WithoutItems_DoesNotPublish()
    {
        var order = OrderBuilder.Order(OrderBuilder.Customer());
        _repo.UpdateOrderAsync(Arg.Any<Order>()).Returns((order, new List<OrderItem>()));

        (
            await _controller.UpdateOrder(order.Id, OrderBuilder.UpdateDto("Shipped"))
        ).Result.ShouldBeOfType<OkObjectResult>();
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateOrder_RepoThrows_Returns500()
    {
        _repo.UpdateOrderAsync(Arg.Any<Order>()).ThrowsAsync(new InvalidOperationException());

        (await _controller.UpdateOrder(Guid.NewGuid(), OrderBuilder.UpdateDto("x")))
            .Result.ShouldBeOfType<ObjectResult>()
            .StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task DeleteOrder_Missing_ReturnsNotFound()
    {
        (await _controller.DeleteOrder(Guid.NewGuid())).ShouldBeOfType<NotFoundResult>();
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteOrder_Existing_PublishesOrderDeletedWithItems()
    {
        var item = OrderBuilder.Item();
        var order = OrderBuilder.Order(OrderBuilder.Customer(), (item, 4));
        _repo.DeleteOrderAsync(order.Id).Returns((DeleteOrderStatus.Deleted, order));

        (await _controller.DeleteOrder(order.Id)).ShouldBeOfType<NoContentResult>();
        var published = _published.ShouldHaveSingleItem();
        published.Event.ShouldBe("Order_Deleted");
        published.OrderItems.ShouldBe([new OrderItemPublishedDto(item.ExternalBatchId, 4)]);
    }

    [Fact]
    public async Task DeleteOrder_HasInvoice_Returns409WithMessageAndDoesNotPublish()
    {
        var order = OrderBuilder.Order(OrderBuilder.Customer());
        _repo.DeleteOrderAsync(order.Id).Returns((DeleteOrderStatus.HasInvoice, order));

        var result = (await _controller.DeleteOrder(order.Id)).ShouldBeOfType<ObjectResult>();

        result.StatusCode.ShouldBe(409);
        var problem = result.Value.ShouldBeOfType<ProblemDetails>();
        problem.Title.ShouldBe("Order has an invoice");
        problem.Detail.ShouldBe(
            $"Order {order.Id} has an invoice. Delete the invoice before deleting the order."
        );
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteOrder_PublishFails_StillReturnsNoContent()
    {
        var order = OrderBuilder.Order(OrderBuilder.Customer());
        _repo.DeleteOrderAsync(order.Id).Returns((DeleteOrderStatus.Deleted, order));
        _bus.PublishNewOrderAsync(Arg.Any<OrderPublishedEvent>())
            .ThrowsAsync(new InvalidOperationException());

        (await _controller.DeleteOrder(order.Id)).ShouldBeOfType<NoContentResult>();
    }
}
