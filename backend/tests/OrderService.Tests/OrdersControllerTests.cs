using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ExceptionExtensions;
using OrderService.AsyncDataServices;
using OrderService.Controllers;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.Mappings;
using OrderService.Models;
using OrderService.SyncDataServices.Grpc;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public class OrdersControllerTests
{
    private readonly IOrderRepo _repo = Substitute.For<IOrderRepo>();
    private readonly IMessageBusClient _bus = Substitute.For<IMessageBusClient>();
    private readonly IInventoryDataClient _inventory = Substitute.For<IInventoryDataClient>();
    private readonly ICustomerDataClient _customers = Substitute.For<ICustomerDataClient>();
    private readonly FakeTimeProvider _clock;
    private readonly OrdersController _controller;
    private readonly List<OrderPublishedEvent> _published = [];

    // Known to the substituted Customer and Inventory services.
    private readonly Guid _customerId = TestData.Faker.Random.Guid();
    private readonly CustomerSnapshot _customer = OrderBuilder.Customer();
    private readonly ProductSnapshot _product = OrderBuilder.Product();
    private readonly BatchSnapshot[] _batches;

    public OrdersControllerTests()
    {
        _clock = new FakeTimeProvider(new DateTimeOffset(TestData.Date(2026, 2027), TimeSpan.Zero));
        _controller = new OrdersController(_repo, _bus, _inventory, _customers, _clock);
        _bus.PublishNewOrderAsync(Arg.Do<OrderPublishedEvent>(_published.Add));
        _batches = [OrderBuilder.Batch(_product.Id), OrderBuilder.Batch(_product.Id)];
        _customers.GetCustomerAsync(_customerId).Returns(_customer);
        _inventory.Serves(OrderBuilder.Catalog([_product], _batches));
    }

    private static UpstreamUnavailableException Unavailable(string service) =>
        new(service, new InvalidOperationException());

    private static ProblemDetails ShouldBeProblem(IConvertToActionResult result, int status)
    {
        var objectResult = result.Convert().ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(status);
        return objectResult.Value.ShouldBeOfType<ProblemDetails>();
    }

    private Order ExistingOrder(OrderStatus status = OrderStatus.InProcess)
    {
        var order = OrderBuilder.OrderFor(_customerId, OrderBuilder.Item(batchCount: 2));
        order.Status = status;
        _repo.GetOrderForUpdateAsync(order.Id).Returns(order);
        return order;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private static Invoice InvoiceOf(Order order) =>
        new()
        {
            Id = TestData.Faker.Random.Int(1, 1000),
            OrderId = order.Id,
            Order = order,
            InvoiceDate = TestData.Date(2025, 2026),
            InvoiceStatus = "Unpaid",
            TotalAmount = order.Items.Gross(),
        };

    // (product, batch, quantity) per item batch, in order.
    private static List<(Guid ProductId, Guid BatchId, int Quantity)> Payload(
        IEnumerable<OrderItemPublishedDto> items
    ) => [.. items.SelectMany(l => l.Batches.Select(b => (l.ProductId, b.BatchId, b.Quantity)))];

    private static List<(Guid ProductId, Guid BatchId, int Quantity)> Payload(
        IEnumerable<OrderItem> items
    ) => [.. items.SelectMany(l => l.Batches.Select(b => (l.ProductId, b.BatchId, b.Quantity)))];

    private static readonly OrderFilter NoFilter = new(null, null, null, null);

    [Fact]
    public async Task GetAllOrders_NoFilter_BuildsPage()
    {
        var orders = new[] { OrderBuilder.Order(OrderBuilder.Item()), OrderBuilder.Order() };
        _repo.GetOrdersAsync(NoFilter, null, 2).Returns(orders);
        _repo.GetOrderCountAsync(NoFilter).Returns(3);

        var result = await _controller.GetAllOrdersAsync(null, null, null, null, null, 2);

        var page = result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderPageReadDto>();
        page.Orders.Select(o => o.Id).ShouldBe(orders.Select(o => o.Id));
        page.Orders.First().Items.ShouldHaveSingleItem();
        page.Cursor.ShouldBe(orders[1].Id);
        page.Pages.ShouldBe(2);
    }

    [Fact]
    public async Task GetAllOrders_WithFilters_PassesTheFilterAndCountsFilteredPages()
    {
        var f = TestData.Faker;
        var cursor = f.Random.Guid();
        var status = f.PickRandom<OrderStatus>();
        var from = DateOnly.FromDateTime(TestData.Date(2024, 2025));
        var to = from.AddDays(f.Random.Int(0, 60));
        var filter = new OrderFilter(status, _customerId, from, to);
        var order = OrderBuilder.OrderFor(_customerId);
        order.Status = status;
        var count = f.Random.Int(11, 95);
        _repo.GetOrdersAsync(filter, cursor, 10).Returns([order]);
        _repo.GetOrderCountAsync(filter).Returns(count);

        var result = await _controller.GetAllOrdersAsync(cursor, _customerId, status, from, to);

        var page = result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderPageReadDto>();
        var dto = page.Orders.ShouldHaveSingleItem();
        dto.CustomerId.ShouldBe(_customerId);
        dto.Status.ShouldBe(status);
        page.Pages.ShouldBe((count + 9) / 10);
        await _repo.Received(1).GetOrdersAsync(filter, cursor, 10);
        await _repo.Received(1).GetOrderCountAsync(filter);
        await _repo.DidNotReceive().GetOrderCountAsync(NoFilter);
    }

    [Fact]
    public async Task GetAllOrders_Empty_ReturnsNotFound()
    {
        _repo.GetOrdersAsync(NoFilter, null, 10).Returns([]);

        (
            await _controller.GetAllOrdersAsync(null, null, null, null, null)
        ).Result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetAllOrders_RepoThrows_Returns500()
    {
        _repo
            .GetOrdersAsync(Arg.Any<OrderFilter>(), Arg.Any<Guid?>(), Arg.Any<int>())
            .ThrowsAsync(new InvalidOperationException());

        (await _controller.GetAllOrdersAsync(null, null, null, null, null))
            .Result.ShouldBeOfType<ObjectResult>()
            .StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task GetOrderById_MissingAndExisting()
    {
        var order = OrderBuilder.Order(OrderBuilder.Item());
        _repo.GetOrderByIdAsync(order.Id).Returns(order);

        (
            await _controller.GetOrderById(TestData.Faker.Random.Guid())
        ).Result.ShouldBeOfType<NotFoundResult>();
        (await _controller.GetOrderById(order.Id))
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderReadDto>()
            .Items.ShouldHaveSingleItem()
            .ProductId.ShouldBe(order.Items[0].ProductId);
    }

    [Fact]
    public async Task CreateOrder_Valid_SnapshotsSavesAndPublishes()
    {
        var orderId = TestData.Faker.Random.Guid();
        Order? saved = null;
        _repo
            .CreateOrderAsync(Arg.Any<Order>())
            .Returns(ci =>
            {
                saved = ci.Arg<Order>();
                saved.Id = orderId;
                return Task.CompletedTask;
            });
        var (q0, q1) = (TestData.Faker.Random.Int(1, 20), TestData.Faker.Random.Int(1, 20));
        var dto = OrderBuilder.CreateDto(
            _customerId,
            OrderBuilder.ItemDto(_product.Id, (_batches[0].Id, q0), (_batches[1].Id, q1))
        );

        var result = await _controller.CreateOrder(dto);

        var created = result.Result.ShouldBeOfType<CreatedAtRouteResult>();
        created.RouteName.ShouldBe(nameof(OrdersController.GetOrderById));
        created.RouteValues.ShouldNotBeNull()["Id"].ShouldBe(orderId);
        var body = created.Value.ShouldBeOfType<OrderReadDto>();
        body.Id.ShouldBe(orderId);
        body.CustomerId.ShouldBe(_customerId);
        body.Customer.ShouldBe(_customer.ToReadDto());
        body.Status.ShouldBe(OrderStatus.InProcess);
        body.CompletedDate.ShouldBeNull();
        body.CancelledDate.ShouldBeNull();
        body.Invoice.ShouldBeNull();
        body.OrderDate.ShouldBe(dto.OrderDate);
        body.ShippingAddress.ShouldBe(dto.ShippingAddress);
        var item = body.Items.ShouldHaveSingleItem();
        item.ProductId.ShouldBe(_product.Id);
        item.Name.ShouldBe(_product.Name);
        item.UnitPrice.ShouldBe(_product.ListPrice);
        item.Quantity.ShouldBe(q0 + q1);
        item.Batches.Select(b => b.Lot).ShouldBe(_batches.Select(b => b.Lot), ignoreOrder: true);

        saved.ShouldNotBeNull().Customer.ShouldBeSameAs(_customer);
        saved.Status.ShouldBe(OrderStatus.InProcess);
        saved.Items.ShouldHaveSingleItem().Batches.Count.ShouldBe(2);
        await _inventory
            .Received(1)
            .GetOrderSnapshotAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { _product.Id })),
                Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.SequenceEqual(new[] { _batches[0].Id, _batches[1].Id })
                )
            );

        var published = _published.ShouldHaveSingleItem();
        published.Event.ShouldBe("Order_Published");
        published.OrderId.ShouldBe(orderId);
        published.CustomerId.ShouldBe(_customerId);
        Payload(published.Items)
            .ShouldBe([(_product.Id, _batches[0].Id, q0), (_product.Id, _batches[1].Id, q1)]);
    }

    [Fact]
    public async Task CreateOrder_UnknownCustomer_Returns400WithoutAskingInventory()
    {
        var unknown = TestData.Faker.Random.Guid();

        var result = await _controller.CreateOrder(
            OrderBuilder.CreateDto(unknown, OrderBuilder.ItemDto(_product.Id, (_batches[0].Id, 1)))
        );

        var problem = ShouldBeProblem(result, 400);
        problem.Title.ShouldBe("Unknown customer");
        problem.Detail.ShouldBe($"Customer {unknown} does not exist.");
        await _inventory.DidNotReceiveWithAnyArgs().GetOrderSnapshotAsync(default!, default!);
        await _repo.DidNotReceiveWithAnyArgs().CreateOrderAsync(default!);
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateOrder_InvalidItems_Returns400WithTheErrors()
    {
        var unknownProduct = TestData.Faker.Random.Guid();
        var unknownBatch = TestData.Faker.Random.Guid();

        var result = await _controller.CreateOrder(
            OrderBuilder.CreateDto(
                _customerId,
                OrderBuilder.ItemDto(_product.Id, (unknownBatch, 1)),
                OrderBuilder.ItemDto(unknownProduct, (_batches[0].Id, 1))
            )
        );

        var problem = ShouldBeProblem(result, 400);
        problem.Title.ShouldBe("Invalid order items");
        problem.Detail.ShouldBe(
            $"Batch {unknownBatch} does not exist. Product {unknownProduct} does not exist."
        );
        await _repo.DidNotReceiveWithAnyArgs().CreateOrderAsync(default!);
        _published.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("CustomerService")]
    [InlineData("InventoryService")]
    public async Task CreateOrder_ServiceUnavailable_Returns503(string service)
    {
        if (service == "CustomerService")
        {
            _customers.GetCustomerAsync(_customerId).ThrowsAsync(Unavailable(service));
        }
        else
        {
            _inventory
                .GetOrderSnapshotAsync(default!, default!)
                .ThrowsAsyncForAnyArgs(Unavailable(service));
        }

        var result = await _controller.CreateOrder(
            OrderBuilder.CreateDto(
                _customerId,
                OrderBuilder.ItemDto(_product.Id, (_batches[0].Id, 1))
            )
        );

        var problem = ShouldBeProblem(result, 503);
        problem.Title.ShouldBe($"{service} unavailable");
        problem.Detail.ShouldNotBeNull().ShouldContain("the order was not saved");
        await _repo.DidNotReceiveWithAnyArgs().CreateOrderAsync(default!);
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateOrder_RepoThrows_Returns500()
    {
        _repo.CreateOrderAsync(Arg.Any<Order>()).ThrowsAsync(new InvalidOperationException());

        var result = await _controller.CreateOrder(
            OrderBuilder.CreateDto(
                _customerId,
                OrderBuilder.ItemDto(_product.Id, (_batches[0].Id, 1))
            )
        );

        result.Result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(500);
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task CreateOrder_PublishFails_StillReturnsCreated()
    {
        _bus.PublishNewOrderAsync(Arg.Any<OrderPublishedEvent>())
            .ThrowsAsync(new InvalidOperationException());

        var result = await _controller.CreateOrder(
            OrderBuilder.CreateDto(
                _customerId,
                OrderBuilder.ItemDto(_product.Id, (_batches[0].Id, 1))
            )
        );

        result.Result.ShouldBeOfType<CreatedAtRouteResult>();
        await _repo.Received(1).CreateOrderAsync(Arg.Any<Order>());
    }

    [Fact]
    public async Task UpdateOrder_Missing_ReturnsNotFound()
    {
        (
            await _controller.UpdateOrder(
                TestData.Faker.Random.Guid(),
                OrderBuilder.AddressUpdateDto()
            )
        ).Result.ShouldBeOfType<NotFoundResult>();
        await _repo.DidNotReceive().SaveChangesAsync();
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateOrder_ScalarFieldsOnly_KeepsItemsAndCustomerAndPublishesFullState()
    {
        var order = ExistingOrder();
        var items = Payload(order.Items);
        var (orderDate, customer) = (order.OrderDate, order.Customer);
        var dto = OrderBuilder.AddressUpdateDto();

        var result = await _controller.UpdateOrder(order.Id, dto);

        var body = result
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderReadDto>();
        body.ShippingAddress.ShouldBe(dto.ShippingAddress);
        body.OrderDate.ShouldBe(orderDate);
        body.Status.ShouldBe(OrderStatus.InProcess);
        order.Customer.ShouldBeSameAs(customer);
        Payload(order.Items).ShouldBe(items);
        await _repo.Received(1).SaveChangesAsync();
        await _customers.DidNotReceiveWithAnyArgs().GetCustomerAsync(Guid.Empty);
        await _inventory.DidNotReceiveWithAnyArgs().GetOrderSnapshotAsync(default!, default!);
        // Always the full current state, so subscribers see a customer change too.
        var published = _published.ShouldHaveSingleItem();
        published.Event.ShouldBe("Order_Published");
        published.OrderId.ShouldBe(order.Id);
        published.CustomerId.ShouldBe(_customerId);
        Payload(published.Items).ShouldBe(items);
    }

    [Fact]
    public async Task UpdateOrder_SameCustomerId_KeepsTheCustomerSnapshot()
    {
        var order = ExistingOrder();
        var snapshot = order.Customer;

        (
            await _controller.UpdateOrder(order.Id, OrderBuilder.UpdateDto(customerId: _customerId))
        ).Result.ShouldBeOfType<OkObjectResult>();

        order.Customer.ShouldBeSameAs(snapshot);
        await _customers.DidNotReceiveWithAnyArgs().GetCustomerAsync(Guid.Empty);
    }

    [Fact]
    public async Task UpdateOrder_NewCustomerId_TakesAFreshCustomerSnapshot()
    {
        var order = OrderBuilder.Order(OrderBuilder.Item());
        _repo.GetOrderForUpdateAsync(order.Id).Returns(order);

        var body = (
            await _controller.UpdateOrder(order.Id, OrderBuilder.UpdateDto(customerId: _customerId))
        )
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderReadDto>();

        body.CustomerId.ShouldBe(_customerId);
        body.Customer.ShouldBe(_customer.ToReadDto());
        order.Customer.ShouldBeSameAs(_customer);
        await _customers.Received(1).GetCustomerAsync(_customerId);
        _published.ShouldHaveSingleItem().CustomerId.ShouldBe(_customerId);
    }

    [Fact]
    public async Task UpdateOrder_UnknownNewCustomer_Returns400AndDoesNotSave()
    {
        var order = ExistingOrder();
        var snapshot = order.Customer;
        var unknown = TestData.Faker.Random.Guid();

        var problem = ShouldBeProblem(
            await _controller.UpdateOrder(order.Id, OrderBuilder.UpdateDto(customerId: unknown)),
            400
        );

        problem.Title.ShouldBe("Unknown customer");
        problem.Detail.ShouldBe($"Customer {unknown} does not exist.");
        order.CustomerId.ShouldBe(_customerId);
        order.Customer.ShouldBeSameAs(snapshot);
        await _repo.DidNotReceive().SaveChangesAsync();
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateOrder_OnlyExistingProductsAndBatches_DoesNotAskInventory()
    {
        var order = ExistingOrder();
        var item = order.Items[0];
        var kept = item.Batches[0];
        var quantity = kept.Quantity + TestData.Faker.Random.Int(1, 10);
        var price = TestData.Faker.Finance.Amount(1, 100);

        var body = (
            await _controller.UpdateOrder(
                order.Id,
                OrderBuilder.UpdateDto(
                    items: OrderBuilder.ItemDto(item.ProductId, (kept.BatchId, quantity)) with
                    {
                        UnitPrice = price,
                    }
                )
            )
        )
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderReadDto>();

        await _inventory.DidNotReceiveWithAnyArgs().GetOrderSnapshotAsync(default!, default!);
        var dtoItem = body.Items.ShouldHaveSingleItem();
        dtoItem.UnitPrice.ShouldBe(price);
        dtoItem.Name.ShouldBe(item.Name);
        dtoItem.Batches.ShouldHaveSingleItem().Quantity.ShouldBe(quantity);
        order.UpdatedAt.ShouldNotBeNull();
        await _repo.Received(1).SaveChangesAsync();
        Payload(_published.ShouldHaveSingleItem().Items)
            .ShouldBe([(item.ProductId, kept.BatchId, quantity)]);
    }

    [Fact]
    public async Task UpdateOrder_NewProduct_SnapshotsOnlyWhatIsNew()
    {
        var order = ExistingOrder();
        var existing = order.Items[0];

        var body = (
            await _controller.UpdateOrder(
                order.Id,
                OrderBuilder.UpdateDto(
                    null,
                    existing.ToWriteDto(),
                    OrderBuilder.ItemDto(_product.Id, (_batches[1].Id, 4))
                )
            )
        )
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderReadDto>();

        await _inventory
            .Received(1)
            .GetOrderSnapshotAsync(
                Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { _product.Id })),
                Arg.Is<IReadOnlyCollection<Guid>>(ids =>
                    ids.SequenceEqual(new[] { _batches[1].Id })
                )
            );
        body.Items.Select(l => (l.ProductId, l.Name))
            .ShouldBe(
                [(existing.ProductId, existing.Name), (_product.Id, _product.Name)],
                ignoreOrder: true
            );
        Payload(_published.ShouldHaveSingleItem().Items)
            .ShouldBe([.. Payload([existing]), (_product.Id, _batches[1].Id, 4)]);
    }

    [Fact]
    public async Task UpdateOrder_ItemsReplaceTheOrdersItems()
    {
        var order = ExistingOrder();

        (
            await _controller.UpdateOrder(
                order.Id,
                OrderBuilder.UpdateDto(
                    items: OrderBuilder.ItemDto(_product.Id, (_batches[0].Id, 2))
                )
            )
        ).Result.ShouldBeOfType<OkObjectResult>();

        order.Items.ShouldHaveSingleItem().ProductId.ShouldBe(_product.Id);
        Payload(_published.ShouldHaveSingleItem().Items)
            .ShouldBe([(_product.Id, _batches[0].Id, 2)]);
    }

    [Fact]
    public async Task UpdateOrder_InvalidItems_Returns400AndDoesNotSave()
    {
        var order = ExistingOrder();
        var before = Payload(order.Items);
        var unknown = TestData.Faker.Random.Guid();

        var problem = ShouldBeProblem(
            await _controller.UpdateOrder(
                order.Id,
                OrderBuilder.UpdateDto(items: OrderBuilder.ItemDto(unknown, (_batches[0].Id, 1)))
            ),
            400
        );

        problem.Title.ShouldBe("Invalid order items");
        problem.Detail.ShouldBe($"Product {unknown} does not exist.");
        Payload(order.Items).ShouldBe(before);
        await _repo.DidNotReceive().SaveChangesAsync();
        _published.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("CustomerService")]
    [InlineData("InventoryService")]
    public async Task UpdateOrder_ServiceUnavailable_Returns503AndDoesNotSave(string service)
    {
        var order = OrderBuilder.Order(OrderBuilder.Item());
        _repo.GetOrderForUpdateAsync(order.Id).Returns(order);
        _customers.GetCustomerAsync(_customerId).ThrowsAsync(Unavailable("CustomerService"));
        _inventory
            .GetOrderSnapshotAsync(default!, default!)
            .ThrowsAsyncForAnyArgs(Unavailable("InventoryService"));
        var dto =
            service == "CustomerService"
                ? OrderBuilder.UpdateDto(customerId: _customerId)
                : OrderBuilder.UpdateDto(
                    items: OrderBuilder.ItemDto(_product.Id, (_batches[0].Id, 1))
                );

        var problem = ShouldBeProblem(await _controller.UpdateOrder(order.Id, dto), 503);

        problem.Title.ShouldBe($"{service} unavailable");
        await _repo.DidNotReceive().SaveChangesAsync();
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task UpdateOrder_SaveThrows_Returns500()
    {
        var order = ExistingOrder();
        _repo.SaveChangesAsync().ThrowsAsync(new InvalidOperationException());

        (await _controller.UpdateOrder(order.Id, OrderBuilder.AddressUpdateDto()))
            .Result.ShouldBeOfType<ObjectResult>()
            .StatusCode.ShouldBe(500);
        _published.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.InProcess)]
    [InlineData(OrderStatus.Pending)]
    public async Task UpdateOrder_EditableStatus_IsSaved(OrderStatus status)
    {
        var order = ExistingOrder(status);
        var dto = OrderBuilder.AddressUpdateDto();

        var body = (await _controller.UpdateOrder(order.Id, dto))
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderReadDto>();

        body.Status.ShouldBe(status);
        body.ShippingAddress.ShouldBe(dto.ShippingAddress);
        await _repo.Received(1).SaveChangesAsync();
    }

    [Theory]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task UpdateOrder_LockedOrder_Returns409WithoutAskingServicesOrSaving(
        OrderStatus status
    )
    {
        var order = OrderBuilder.Order(OrderBuilder.Item());
        order.Status = status;
        _repo.GetOrderForUpdateAsync(order.Id).Returns(order);
        var (address, items) = (order.ShippingAddress, Payload(order.Items));

        var problem = ShouldBeProblem(
            await _controller.UpdateOrder(
                order.Id,
                new OrderUpdateDto(
                    TestData.Date(2024, 2026),
                    OrderBuilder.ShippingAddress(),
                    _customerId,
                    [OrderBuilder.ItemDto(_product.Id, (_batches[0].Id, 1))]
                )
            ),
            409
        );

        problem.Title.ShouldBe("Order is locked");
        problem.Detail.ShouldNotBeNull().ShouldContain(status.ToString());
        order.ShippingAddress.ShouldBe(address);
        order.CustomerId.ShouldNotBe(_customerId);
        Payload(order.Items).ShouldBe(items);
        await _customers.DidNotReceiveWithAnyArgs().GetCustomerAsync(Guid.Empty);
        await _inventory.DidNotReceiveWithAnyArgs().GetOrderSnapshotAsync(default!, default!);
        await _repo.DidNotReceive().SaveChangesAsync();
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task ChangeStatus_Missing_ReturnsNotFound()
    {
        (
            await _controller.ChangeStatus(
                TestData.Faker.Random.Guid(),
                OrderBuilder.StatusDto(OrderStatus.Pending)
            )
        ).Result.ShouldBeOfType<NotFoundResult>();

        await _repo.DidNotReceive().SaveChangesAsync();
        _published.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.InProcess)]
    [InlineData(OrderStatus.Pending)]
    [InlineData(OrderStatus.Completed)]
    [InlineData(OrderStatus.Cancelled)]
    public async Task ChangeStatus_SameStatus_ReturnsTheOrderWithoutSavingOrPublishing(
        OrderStatus status
    )
    {
        var order = ExistingOrder(status);

        var body = (await _controller.ChangeStatus(order.Id, OrderBuilder.StatusDto(status)))
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderReadDto>();

        body.Id.ShouldBe(order.Id);
        body.Status.ShouldBe(status);
        order.Invoice.ShouldBeNull();
        await _repo.DidNotReceive().SaveChangesAsync();
        _published.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.Completed, OrderStatus.InProcess)]
    [InlineData(OrderStatus.Completed, OrderStatus.Pending)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.InProcess)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Completed)]
    public async Task ChangeStatus_RefusedTransition_Returns409WithTheReason(
        OrderStatus from,
        OrderStatus to
    )
    {
        var order = ExistingOrder(from);

        var problem = ShouldBeProblem(
            await _controller.ChangeStatus(order.Id, OrderBuilder.StatusDto(to)),
            409
        );

        problem.Title.ShouldBe("Status change not allowed");
        problem.Detail.ShouldBe($"An order can't go from {from} to {to}.");
        order.Status.ShouldBe(from);
        await _repo.DidNotReceive().SaveChangesAsync();
        _published.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.InProcess, OrderStatus.Pending)]
    [InlineData(OrderStatus.Pending, OrderStatus.InProcess)]
    public async Task ChangeStatus_BetweenInProcessAndPending_SavesWithoutPublishing(
        OrderStatus from,
        OrderStatus to
    )
    {
        var order = ExistingOrder(from);

        var body = (await _controller.ChangeStatus(order.Id, OrderBuilder.StatusDto(to)))
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderReadDto>();

        body.Status.ShouldBe(to);
        body.CompletedDate.ShouldBeNull();
        body.CancelledDate.ShouldBeNull();
        body.Invoice.ShouldBeNull();
        order.Status.ShouldBe(to);
        await _repo.Received(1).SaveChangesAsync();
        _published.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.InProcess)]
    [InlineData(OrderStatus.Pending)]
    public async Task ChangeStatus_ToCompleted_CreatesTheInvoiceWithoutPublishing(OrderStatus from)
    {
        var order = ExistingOrder(from);
        order.Items.Add(OrderBuilder.Item());
        var gross = order.Items.Gross();

        var body = (
            await _controller.ChangeStatus(order.Id, OrderBuilder.StatusDto(OrderStatus.Completed))
        )
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderReadDto>();

        body.Status.ShouldBe(OrderStatus.Completed);
        body.CompletedDate.ShouldBe(Now);
        body.CancelledDate.ShouldBeNull();
        var invoice = body.Invoice.ShouldNotBeNull();
        invoice.InvoiceDate.ShouldBe(Now);
        invoice.InvoiceStatus.ShouldBe("Unpaid");
        invoice.TotalAmount.ShouldBe(gross);
        invoice.TotalAmount.ShouldBe(body.Totals.Gross);
        order.Invoice.ShouldNotBeNull().OrderId.ShouldBe(order.Id);
        order.CompletedDate.ShouldBe(Now);
        await _repo.Received(1).SaveChangesAsync();
        _published.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(OrderStatus.InProcess)]
    [InlineData(OrderStatus.Pending)]
    public async Task ChangeStatus_ToCompletedWithAnInvoice_Returns409(OrderStatus from)
    {
        var order = ExistingOrder(from);
        var invoice = InvoiceOf(order);
        order.Invoice = invoice;

        var problem = ShouldBeProblem(
            await _controller.ChangeStatus(order.Id, OrderBuilder.StatusDto(OrderStatus.Completed)),
            409
        );

        problem.Title.ShouldBe("Status change not allowed");
        problem.Detail.ShouldBe("The order already has an invoice.");
        order.Invoice.ShouldBeSameAs(invoice);
        order.CompletedDate.ShouldBeNull();
        await _repo.DidNotReceive().SaveChangesAsync();
    }

    [Theory]
    [InlineData(OrderStatus.InProcess, false)]
    [InlineData(OrderStatus.Pending, false)]
    [InlineData(OrderStatus.Completed, true)]
    public async Task ChangeStatus_ToCancelled_SetsCancelledDateAndPublishesOrderDeleted(
        OrderStatus from,
        bool hasInvoice
    )
    {
        var order = ExistingOrder(from);
        order.Items.Add(OrderBuilder.Item(batchCount: 3));
        var invoice = hasInvoice ? InvoiceOf(order) : null;
        order.Invoice = invoice;
        var completedDate = hasInvoice ? TestData.Date(2025, 2026) : (DateTime?)null;
        order.CompletedDate = completedDate;

        var body = (
            await _controller.ChangeStatus(order.Id, OrderBuilder.StatusDto(OrderStatus.Cancelled))
        )
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderReadDto>();

        body.Status.ShouldBe(OrderStatus.Cancelled);
        body.CancelledDate.ShouldBe(Now);
        body.CompletedDate.ShouldBe(completedDate);
        // Cancelling keeps an existing invoice.
        order.Invoice.ShouldBeSameAs(invoice);
        await _repo.Received(1).SaveChangesAsync();
        var published = _published.ShouldHaveSingleItem();
        published.Event.ShouldBe("Order_Deleted");
        published.OrderId.ShouldBe(order.Id);
        published.CustomerId.ShouldBe(_customerId);
        Payload(published.Items).ShouldBe(Payload(order.Items));
    }

    [Fact]
    public async Task ChangeStatus_CancelledToPending_ClearsCancelledDateAndPublishesOrderPublished()
    {
        var order = ExistingOrder(OrderStatus.Cancelled);
        order.CancelledDate = TestData.Date(2025, 2026);

        var body = (
            await _controller.ChangeStatus(order.Id, OrderBuilder.StatusDto(OrderStatus.Pending))
        )
            .Result.ShouldBeOfType<OkObjectResult>()
            .Value.ShouldBeOfType<OrderReadDto>();

        body.Status.ShouldBe(OrderStatus.Pending);
        body.CancelledDate.ShouldBeNull();
        order.CancelledDate.ShouldBeNull();
        await _repo.Received(1).SaveChangesAsync();
        var published = _published.ShouldHaveSingleItem();
        published.Event.ShouldBe("Order_Published");
        published.OrderId.ShouldBe(order.Id);
        published.CustomerId.ShouldBe(_customerId);
        Payload(published.Items).ShouldBe(Payload(order.Items));
    }

    [Fact]
    public async Task ChangeStatus_CancelledWithInvoiceToPending_Returns409()
    {
        var order = ExistingOrder(OrderStatus.Cancelled);
        var cancelledDate = TestData.Date(2025, 2026);
        order.CancelledDate = cancelledDate;
        order.Invoice = InvoiceOf(order);

        var problem = ShouldBeProblem(
            await _controller.ChangeStatus(order.Id, OrderBuilder.StatusDto(OrderStatus.Pending)),
            409
        );

        problem.Title.ShouldBe("Status change not allowed");
        problem.Detail.ShouldBe(
            "A cancelled order with an invoice can't be reopened; delete the invoice first."
        );
        order.Status.ShouldBe(OrderStatus.Cancelled);
        order.CancelledDate.ShouldBe(cancelledDate);
        await _repo.DidNotReceive().SaveChangesAsync();
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task ChangeStatus_SaveThrows_Returns500WithoutPublishing()
    {
        var order = ExistingOrder(OrderStatus.Pending);
        _repo.SaveChangesAsync().ThrowsAsync(new InvalidOperationException());

        (await _controller.ChangeStatus(order.Id, OrderBuilder.StatusDto(OrderStatus.Cancelled)))
            .Result.ShouldBeOfType<ObjectResult>()
            .StatusCode.ShouldBe(500);
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task ChangeStatus_PublishFails_StillReturnsOk()
    {
        var order = ExistingOrder(OrderStatus.Pending);
        _bus.PublishNewOrderAsync(Arg.Any<OrderPublishedEvent>())
            .ThrowsAsync(new InvalidOperationException());

        (
            await _controller.ChangeStatus(order.Id, OrderBuilder.StatusDto(OrderStatus.Cancelled))
        ).Result.ShouldBeOfType<OkObjectResult>();
        order.Status.ShouldBe(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task DeleteOrder_Missing_ReturnsNotFound()
    {
        _repo.DeleteOrderAsync(Arg.Any<Guid>()).Returns((DeleteOrderStatus.NotFound, (Order?)null));

        (
            await _controller.DeleteOrder(TestData.Faker.Random.Guid())
        ).ShouldBeOfType<NotFoundResult>();
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteOrder_Existing_PublishesOrderDeletedWithItems()
    {
        var order = OrderBuilder.Order(OrderBuilder.Item(batchCount: 2), OrderBuilder.Item());
        _repo.DeleteOrderAsync(order.Id).Returns((DeleteOrderStatus.Deleted, order));

        (await _controller.DeleteOrder(order.Id)).ShouldBeOfType<NoContentResult>();

        var published = _published.ShouldHaveSingleItem();
        published.Event.ShouldBe("Order_Deleted");
        published.OrderId.ShouldBe(order.Id);
        published.CustomerId.ShouldBe(order.CustomerId!.Value);
        Payload(published.Items).ShouldBe(Payload(order.Items));
    }

    [Fact]
    public async Task DeleteOrder_HasInvoice_Returns409WithMessageAndDoesNotPublish()
    {
        var order = OrderBuilder.Order(OrderBuilder.Item());
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
    public async Task DeleteOrder_RepoThrows_Returns500()
    {
        _repo.DeleteOrderAsync(Arg.Any<Guid>()).ThrowsAsync(new InvalidOperationException());

        (await _controller.DeleteOrder(TestData.Faker.Random.Guid()))
            .ShouldBeOfType<ObjectResult>()
            .StatusCode.ShouldBe(500);
        _published.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeleteOrder_PublishFails_StillReturnsNoContent()
    {
        var order = OrderBuilder.Order(OrderBuilder.Item());
        _repo.DeleteOrderAsync(order.Id).Returns((DeleteOrderStatus.Deleted, order));
        _bus.PublishNewOrderAsync(Arg.Any<OrderPublishedEvent>())
            .ThrowsAsync(new InvalidOperationException());

        (await _controller.DeleteOrder(order.Id)).ShouldBeOfType<NoContentResult>();
    }
}
