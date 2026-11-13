using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute.ExceptionExtensions;
using OrderService.AsyncDataServices;
using OrderService.Dtos;
using OrderService.Mappings;
using OrderService.Models;
using OrderService.SyncDataServices.Grpc;
using Sava.Tests.Shared;

namespace OrderService.Tests;

[Trait("Category", "Integration")]
public class OrderApiTests(OrderApiFactory factory) : IClassFixture<OrderApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // A item for factory.Products[product] taking quantity from its first batch.
    private OrderItemWriteDto ItemDto(int product, int quantity = 2) =>
        OrderBuilder.ItemDto(
            factory.Products[product].Id,
            (factory.Batches[2 * product].Id, quantity)
        );

    private async Task<OrderReadDto> CreateOrderAsync(params OrderItemWriteDto[] items)
    {
        var dto = OrderBuilder.CreateDto(
            factory.CustomerId,
            items.Length == 0 ? [ItemDto(0)] : items
        );
        var response = await _client.PostAsJsonAsync("/api/orders", dto, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<OrderReadDto>(Ct)).ShouldNotBeNull();
    }

    private async Task<OrderReadDto> GetOrderAsync(Guid id) =>
        (await _client.GetFromJsonAsync<OrderReadDto>($"/api/orders/{id}", Ct)).ShouldNotBeNull();

    private Task<HttpResponseMessage> ChangeStatusAsync(Guid id, OrderStatus status) =>
        _client.PostAsJsonAsync($"/api/orders/{id}/status", OrderBuilder.StatusDto(status), Ct);

    private async Task<OrderReadDto> ChangeStatusOkAsync(Guid id, OrderStatus status)
    {
        var response = await ChangeStatusAsync(id, status);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var order = (await response.Content.ReadFromJsonAsync<OrderReadDto>(Ct)).ShouldNotBeNull();
        order.Status.ShouldBe(status);
        return order;
    }

    // The events written to the outbox for the order so far, oldest first.
    private Task<List<OrderPublishedEvent>> PublishedForAsync(Guid orderId) =>
        factory.OutboxFor(orderId);

    private static async Task<ProblemDetails> ShouldBeProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status
    )
    {
        response.StatusCode.ShouldBe(status);
        return (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct)).ShouldNotBeNull();
    }

    [Fact]
    public async Task CompletingOrdersConcurrently_GivesEachInvoiceItsOwnNumber()
    {
        var orders = new List<OrderReadDto>();
        for (var i = 0; i < 8; i++)
        {
            orders.Add(await CreateOrderAsync());
        }

        var completed = await Task.WhenAll(
            orders.Select(o => ChangeStatusOkAsync(o.Id, OrderStatus.Completed))
        );

        completed.Select(o => o.Invoice.ShouldNotBeNull().Number).ShouldBeUnique();
    }

    [Fact]
    public async Task CreateOrder_SnapshotsCustomerAndProducts()
    {
        var product = factory.Products[0];
        var (first, second) = (factory.Batches[0], factory.Batches[1]);
        var (q0, q1) = (TestData.Faker.Random.Int(1, 20), TestData.Faker.Random.Int(1, 20));

        var order = await CreateOrderAsync(
            OrderBuilder.ItemDto(product.Id, (first.Id, q0), (second.Id, q1))
        );

        order.CustomerId.ShouldBe(factory.CustomerId);
        order.Customer.ShouldBe(factory.Customer.ToReadDto());
        var item = order.Items.ShouldHaveSingleItem();
        (item.ProductId, item.SKU, item.Name, item.UnitOfMeasure).ShouldBe(
            (product.Id, product.SKU, product.Name, product.UnitOfMeasure)
        );
        (item.ListPrice, item.UnitPrice, item.VatRate, item.Cost).ShouldBe(
            (product.ListPrice, product.ListPrice, product.VatRate, product.Cost)
        );
        item.Quantity.ShouldBe(q0 + q1);
        item.Batches.Select(b => (b.BatchId, b.Lot, b.ExpirationDate, b.Quantity))
            .ShouldBe(
                [
                    (first.Id, first.Lot, first.ExpirationDate, q0),
                    (second.Id, second.Lot, second.ExpirationDate, q1),
                ],
                ignoreOrder: true
            );
        order.Totals.Net.ShouldBe(item.Net);
        order.Totals.Gross.ShouldBe(item.Gross);
        (await GetOrderAsync(order.Id)).Items.ShouldHaveSingleItem().Batches.Count.ShouldBe(2);
        var published = (await PublishedForAsync(order.Id)).ShouldHaveSingleItem();
        published.Event.ShouldBe("Order_Published");
        published.MessageId.ShouldNotBe(Guid.Empty);
        published.CustomerId.ShouldBe(factory.CustomerId);
        var publishedItem = published.Items.ShouldHaveSingleItem();
        publishedItem.ProductId.ShouldBe(product.Id);
        publishedItem.Batches.Sum(b => b.Quantity).ShouldBe(q0 + q1);
    }

    [Fact]
    public async Task CreateOrder_UnknownCustomer_Returns400()
    {
        var unknown = TestData.Faker.Random.Guid();

        var problem = await ShouldBeProblemAsync(
            await _client.PostAsJsonAsync(
                "/api/orders",
                OrderBuilder.CreateDto(unknown, ItemDto(0)),
                Ct
            ),
            HttpStatusCode.BadRequest
        );

        problem.Title.ShouldBe("Unknown customer");
        problem.Detail.ShouldBe($"Customer {unknown} does not exist.");
    }

    [Fact]
    public async Task CreateOrder_UnknownProductOrForeignBatch_Returns400()
    {
        var unknown = TestData.Faker.Random.Guid();
        var dto = OrderBuilder.CreateDto(
            factory.CustomerId,
            OrderBuilder.ItemDto(unknown, (factory.Batches[0].Id, 1)),
            OrderBuilder.ItemDto(factory.Products[1].Id, (factory.Batches[1].Id, 1))
        );

        var problem = await ShouldBeProblemAsync(
            await _client.PostAsJsonAsync("/api/orders", dto, Ct),
            HttpStatusCode.BadRequest
        );

        problem.Title.ShouldBe("Invalid order items");
        problem.Detail.ShouldBe(
            $"Product {unknown} does not exist. "
                + $"Batch {factory.Batches[1].Id} belongs to another product than {factory.Products[1].Id}."
        );
    }

    [Fact]
    public async Task CreateOrder_CustomerServiceUnavailable_Returns503()
    {
        var customerId = TestData.Faker.Random.Guid();
        factory
            .CustomerClient.GetCustomerAsync(customerId)
            .ThrowsAsync(
                new UpstreamUnavailableException("CustomerService", new InvalidOperationException())
            );

        var problem = await ShouldBeProblemAsync(
            await _client.PostAsJsonAsync(
                "/api/orders",
                OrderBuilder.CreateDto(customerId, ItemDto(0)),
                Ct
            ),
            HttpStatusCode.ServiceUnavailable
        );

        problem.Title.ShouldBe("CustomerService unavailable");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateOrder_InvalidQuantity_Returns400(int quantity)
    {
        var dto = OrderBuilder.CreateDto(factory.CustomerId, ItemDto(0, quantity));

        (await _client.PostAsJsonAsync("/api/orders", dto, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.BadRequest
        );
    }

    [Fact]
    public async Task CreateOrder_MissingCustomerOrItems_Returns400()
    {
        var noCustomer = OrderBuilder.CreateDto(factory.CustomerId, ItemDto(0)) with
        {
            CustomerId = null,
        };
        var noItems = OrderBuilder.CreateDto(factory.CustomerId);
        var lineWithoutBatches = OrderBuilder.CreateDto(
            factory.CustomerId,
            OrderBuilder.ItemDto(factory.Products[0].Id)
        );

        foreach (var dto in new[] { noCustomer, noItems, lineWithoutBatches })
        {
            var response = await _client.PostAsJsonAsync("/api/orders", dto, Ct);
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    [Fact]
    public async Task CreateOrder_DuplicateProducts_Returns400()
    {
        var productId = factory.Products[0].Id;
        var dto = OrderBuilder.CreateDto(
            factory.CustomerId,
            OrderBuilder.ItemDto(productId, (factory.Batches[0].Id, 1)),
            OrderBuilder.ItemDto(productId, (factory.Batches[1].Id, 2))
        );

        var response = await _client.PostAsJsonAsync("/api/orders", dto, Ct);

        await ShouldBeDuplicatesProblemAsync(response, productId);
    }

    [Fact]
    public async Task UpdateOrder_DuplicateBatches_Returns400AndKeepsOrder()
    {
        var order = await CreateOrderAsync();
        var batchId = factory.Batches[1].Id;

        var response = await _client.PutAsJsonAsync(
            $"/api/orders/{order.Id}",
            OrderBuilder.UpdateDto(
                null,
                OrderBuilder.ItemDto(factory.Products[0].Id, (batchId, 1), (batchId, 3))
            ),
            Ct
        );

        await ShouldBeDuplicatesProblemAsync(response, batchId);
        (await GetOrderAsync(order.Id))
            .Items.ShouldHaveSingleItem()
            .Batches.ShouldHaveSingleItem()
            .BatchId.ShouldBe(factory.Batches[0].Id);
    }

    private static async Task ShouldBeDuplicatesProblemAsync(HttpResponseMessage response, Guid id)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (
            await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct)
        ).ShouldNotBeNull();
        problem.Errors.ShouldContainKey("Items");
        problem.Errors["Items"].ShouldHaveSingleItem().ShouldContain(id.ToString());
    }

    [Fact]
    public async Task UpdateOrder_InvalidItems_Returns400AndKeepsOrder()
    {
        var order = await CreateOrderAsync();
        var unknownBatch = TestData.Faker.Random.Guid();

        var problem = await ShouldBeProblemAsync(
            await _client.PutAsJsonAsync(
                $"/api/orders/{order.Id}",
                new OrderUpdateDto(
                    null,
                    OrderBuilder.ShippingAddress(),
                    null,
                    [OrderBuilder.ItemDto(factory.Products[0].Id, (unknownBatch, 1))]
                ),
                Ct
            ),
            HttpStatusCode.BadRequest
        );

        problem.Detail.ShouldBe($"Batch {unknownBatch} does not exist.");
        var saved = await GetOrderAsync(order.Id);
        saved.ShippingAddress.ShouldBe(order.ShippingAddress);
        saved
            .Items.ShouldHaveSingleItem()
            .Batches.ShouldHaveSingleItem()
            .BatchId.ShouldBe(factory.Batches[0].Id);
    }

    [Fact]
    public async Task UpdateOrder_Unknown_Returns404()
    {
        (
            await _client.PutAsJsonAsync(
                $"/api/orders/{TestData.Faker.Random.Guid()}",
                OrderBuilder.AddressUpdateDto(),
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task GetOrders_PageSizeOutOfRange_Returns400(int pageSize)
    {
        (await _client.GetAsync($"/api/orders?pageSize={pageSize}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.BadRequest
        );
    }

    [Fact]
    public async Task GetOrders_FilteredByCustomer_ReturnsPage()
    {
        await CreateOrderAsync();

        var page = await _client.GetFromJsonAsync<OrderPageReadDto>(
            $"/api/orders?customerId={factory.CustomerId}",
            Ct
        );

        page.ShouldNotBeNull().Orders.ShouldNotBeEmpty();
        page.Orders.ShouldAllBe(o => o.CustomerId == factory.CustomerId && o.Items.Count > 0);
        (
            await _client.GetAsync($"/api/orders?customerId={TestData.Faker.Random.Guid()}", Ct)
        ).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    // Changing quantities of batches already on the order needs no Inventory call; the item keeps
    // the snapshot it was created with.
    [Fact]
    public async Task UpdateOrder_ExistingBatches_KeepTheSnapshotWithoutAskingInventory()
    {
        var order = await CreateOrderAsync();
        var price = TestData.Faker.Finance.Amount(1, 100);
        factory.InventoryClient.ClearReceivedCalls();

        var response = await _client.PutAsJsonAsync(
            $"/api/orders/{order.Id}",
            OrderBuilder.UpdateDto(items: ItemDto(0, 9) with { UnitPrice = price }),
            Ct
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        await factory
            .InventoryClient.DidNotReceiveWithAnyArgs()
            .GetOrderSnapshotAsync(default!, default!);
        var item = (await GetOrderAsync(order.Id)).Items.ShouldHaveSingleItem();
        item.Quantity.ShouldBe(9);
        item.UnitPrice.ShouldBe(price);
        item.ListPrice.ShouldBe(order.Items[0].ListPrice);
        item.Name.ShouldBe(order.Items[0].Name);
    }

    [Fact]
    public async Task GetOrders_FilteredByStatus_ReturnsOnlyThatStatus()
    {
        var pending = await CreateOrderAsync();
        var completed = await CreateOrderAsync();
        var inProcess = await CreateOrderAsync();
        await ChangeStatusOkAsync(pending.Id, OrderStatus.Pending);
        await ChangeStatusOkAsync(completed.Id, OrderStatus.Completed);

        var page = (
            await _client.GetFromJsonAsync<OrderPageReadDto>(
                "/api/orders?status=Completed&pageSize=50",
                Ct
            )
        ).ShouldNotBeNull();

        page.Orders.ShouldAllBe(o => o.Status == OrderStatus.Completed && o.Invoice != null);
        page.Orders.Select(o => o.Id).ShouldContain(completed.Id);
        page.Orders.Select(o => o.Id).ShouldNotContain(pending.Id);
        page.Orders.Select(o => o.Id).ShouldNotContain(inProcess.Id);
        page.Pages.ShouldBe(1);
        var pendingPage = (
            await _client.GetFromJsonAsync<OrderPageReadDto>(
                $"/api/orders?status=Pending&customerId={factory.CustomerId}&pageSize=50",
                Ct
            )
        ).ShouldNotBeNull();
        pendingPage.Orders.ShouldAllBe(o => o.Status == OrderStatus.Pending);
        pendingPage.Orders.Select(o => o.Id).ShouldContain(pending.Id);
        (await _client.GetAsync("/api/orders?status=Shipped", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.BadRequest
        );
    }

    [Fact]
    public async Task GetOrders_FilteredByDays_IncludesTheWholeToDay()
    {
        // A year no other test uses, so the day filter sees only this order.
        var day = DateOnly.FromDateTime(TestData.Date(2030, 2030));
        var at = day.ToDateTime(new TimeOnly(23, 59, 59));
        var dto = OrderBuilder.CreateDto(factory.CustomerId, ItemDto(0)) with { OrderDate = at };
        var created = await _client.PostAsJsonAsync("/api/orders", dto, Ct);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        var order = (await created.Content.ReadFromJsonAsync<OrderReadDto>(Ct)).ShouldNotBeNull();

        var page = (
            await _client.GetFromJsonAsync<OrderPageReadDto>(
                $"/api/orders?from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}",
                Ct
            )
        ).ShouldNotBeNull();

        page.Orders.ShouldHaveSingleItem().Id.ShouldBe(order.Id);
        page.Pages.ShouldBe(1);
        (
            await _client.GetAsync($"/api/orders?from={day.AddDays(1):yyyy-MM-dd}", Ct)
        ).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (
            await _client.GetAsync(
                $"/api/orders?to={day.AddDays(-1):yyyy-MM-dd}&from=2030-01-01",
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ChangeStatus_UnknownOrderOrMissingStatus_Returns404Or400()
    {
        (
            await ChangeStatusAsync(TestData.Faker.Random.Guid(), OrderStatus.Pending)
        ).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var order = await CreateOrderAsync();
        (
            await _client.PostAsJsonAsync(
                $"/api/orders/{order.Id}/status",
                new OrderStatusChangeDto(null),
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await GetOrderAsync(order.Id)).Status.ShouldBe(OrderStatus.InProcess);
    }

    [Fact]
    public async Task OrderAndInvoiceLifecycle()
    {
        var creator = TestData.Faker.Internet.UserName();
        var editor = TestData.Faker.Internet.UserName();
        _client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(
            TestTokens.Bearer(creator)
        );
        var order = await CreateOrderAsync();
        (await PublishedForAsync(order.Id)).Count.ShouldBe(1);
        order.Status.ShouldBe(OrderStatus.InProcess);
        order.Invoice.ShouldBeNull();
        order.Audit.ShouldNotBeNull().CreatedBy.ShouldBe(creator);
        order.Audit.UpdatedBy.ShouldBeNull();
        var productB = factory.Products[1];
        var batchB = factory.Batches[2];

        _client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(
            TestTokens.Bearer(editor)
        );
        var update = await _client.PutAsJsonAsync(
            $"/api/orders/{order.Id}",
            OrderBuilder.UpdateDto(null, ItemDto(1, 5)),
            Ct
        );
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await GetOrderAsync(order.Id);
        updated.Status.ShouldBe(OrderStatus.InProcess);
        updated.ShippingAddress.ShouldBe(order.ShippingAddress);
        var item = updated.Items.ShouldHaveSingleItem();
        item.ProductId.ShouldBe(productB.Id);
        item.Name.ShouldBe(productB.Name);
        item.Batches.ShouldHaveSingleItem().BatchId.ShouldBe(batchB.Id);
        updated.Audit.ShouldNotBeNull().CreatedBy.ShouldBe(creator);
        updated.Audit.UpdatedBy.ShouldBe(editor);
        updated.Audit.UpdatedAt.ShouldNotBeNull();

        // InProcess <-> Pending changes nothing else and publishes nothing.
        (await ChangeStatusOkAsync(order.Id, OrderStatus.Pending)).Invoice.ShouldBeNull();
        (await GetOrderAsync(order.Id)).Status.ShouldBe(OrderStatus.Pending);
        await ChangeStatusOkAsync(order.Id, OrderStatus.InProcess);
        (await PublishedForAsync(order.Id)).Count.ShouldBe(2);

        // Completing creates the invoice and locks the order.
        var completed = await ChangeStatusOkAsync(order.Id, OrderStatus.Completed);
        var completedDate = completed.CompletedDate.ShouldNotBeNull();
        completed.CancelledDate.ShouldBeNull();
        var invoice = completed.Invoice.ShouldNotBeNull();
        invoice.Number.ShouldNotBeNull().ShouldMatch(@"^\d{10}$");
        invoice.InvoiceDate.ShouldBe(completedDate);
        invoice.DueDate.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(completedDate.Date);
        invoice.PaymentStatus.ShouldBe(PaymentStatus.Unpaid);
        invoice.Gross.ShouldBe(updated.Totals.Gross);
        (await PublishedForAsync(order.Id)).Count.ShouldBe(2);
        var saved = await GetOrderAsync(order.Id);
        saved.Status.ShouldBe(OrderStatus.Completed);
        saved.Invoice.ShouldNotBeNull().Id.ShouldBe(invoice.Id);
        // The same status again is a no-op; going back to InProcess isn't allowed.
        (await ChangeStatusOkAsync(order.Id, OrderStatus.Completed))
            .Invoice.ShouldNotBeNull()
            .Id.ShouldBe(invoice.Id);
        var refused = await ShouldBeProblemAsync(
            await ChangeStatusAsync(order.Id, OrderStatus.InProcess),
            HttpStatusCode.Conflict
        );
        refused.Title.ShouldBe("Status change not allowed");

        var locked = await ShouldBeProblemAsync(
            await _client.PutAsJsonAsync(
                $"/api/orders/{order.Id}",
                OrderBuilder.AddressUpdateDto(),
                Ct
            ),
            HttpStatusCode.Conflict
        );
        locked.Title.ShouldBe("Order is locked");
        (await GetOrderAsync(order.Id)).ShippingAddress.ShouldBe(order.ShippingAddress);

        var paid = await _client.PutAsJsonAsync(
            $"/api/invoices/{invoice.Id}",
            new InvoiceUpdateDto(null, null, null, null, null, PaymentStatus.Paid, null, null),
            Ct
        );
        paid.StatusCode.ShouldBe(HttpStatusCode.OK);
        var paidInvoice = (
            await paid.Content.ReadFromJsonAsync<InvoiceReadDto>(Ct)
        ).ShouldNotBeNull();
        paidInvoice.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        paidInvoice.PaidDate.ShouldNotBeNull();
        paidInvoice.Gross.ShouldBe(updated.Totals.Gross);
        paidInvoice.OrderId.ShouldBe(order.Id);
        paidInvoice.OrderStatus.ShouldBe(OrderStatus.Completed);
        paidInvoice.Audit.CreatedBy.ShouldBe(editor);
        paidInvoice.Audit.UpdatedBy.ShouldBe(editor);
        var fetched = (
            await _client.GetFromJsonAsync<InvoiceReadDto>($"/api/invoices/{invoice.Id}", Ct)
        ).ShouldNotBeNull();
        fetched.PaymentStatus.ShouldBe(PaymentStatus.Paid);
        fetched.Items.ShouldHaveSingleItem().ProductId.ShouldBe(productB.Id);
        fetched.Customer.ShouldBe(factory.Customer.ToReadDto());
        (await GetOrderAsync(order.Id))
            .Invoice.ShouldNotBeNull()
            .PaymentStatus.ShouldBe(PaymentStatus.Paid);
        var invoices = await _client.GetFromJsonAsync<InvoicePageReadDto>("/api/invoices", Ct);
        invoices.ShouldNotBeNull().Invoices.ShouldContain(i => i.Id == invoice.Id);

        // An invoiced order cannot be deleted; the rejected attempt leaves it intact.
        var problem = await ShouldBeProblemAsync(
            await _client.DeleteAsync($"/api/orders/{order.Id}", Ct),
            HttpStatusCode.Conflict
        );
        problem.Status.ShouldBe(409);
        problem.Title.ShouldBe("Order has an invoice");
        problem.Detail.ShouldBe(
            $"Order {order.Id} has an invoice. Delete the invoice before deleting the order."
        );
        (await GetOrderAsync(order.Id)).Items.ShouldHaveSingleItem();

        // Deleting the invoice reopens the order as Pending.
        (await _client.DeleteAsync($"/api/invoices/{invoice.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );
        var reopened = await GetOrderAsync(order.Id);
        reopened.Status.ShouldBe(OrderStatus.Pending);
        reopened.CompletedDate.ShouldBeNull();
        reopened.Invoice.ShouldBeNull();
        reopened.Audit.UpdatedBy.ShouldBe(editor);

        // Cancelling releases the stock; reopening takes it again.
        var cancelled = await ChangeStatusOkAsync(order.Id, OrderStatus.Cancelled);
        cancelled.CancelledDate.ShouldNotBeNull();
        (await PublishedForAsync(order.Id)).Count.ShouldBe(3);
        (
            await _client.PutAsJsonAsync(
                $"/api/orders/{order.Id}",
                OrderBuilder.AddressUpdateDto(),
                Ct
            )
        ).StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var pending = await ChangeStatusOkAsync(order.Id, OrderStatus.Pending);
        pending.CancelledDate.ShouldBeNull();
        pending.CompletedDate.ShouldBeNull();
        (await PublishedForAsync(order.Id)).Count.ShouldBe(4);

        _client.DefaultRequestHeaders.Authorization = null;
        (await _client.DeleteAsync($"/api/orders/{order.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );
        (await _client.GetAsync($"/api/orders/{order.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );

        // create, update, cancel, reopen, delete; every later event carries the updated items.
        var events = await PublishedForAsync(order.Id);
        events
            .Select(e => e.Event)
            .ShouldBe([
                "Order_Published",
                "Order_Published",
                "Order_Deleted",
                "Order_Published",
                "Order_Deleted",
            ]);
        events.ShouldAllBe(e => e.CustomerId == factory.CustomerId);
        events[0].Items.ShouldHaveSingleItem().ProductId.ShouldBe(factory.Products[0].Id);
        foreach (var e in events.Skip(1))
        {
            var published = e.Items.ShouldHaveSingleItem();
            published.ProductId.ShouldBe(productB.Id);
            var batch = published.Batches.ShouldHaveSingleItem();
            batch.BatchId.ShouldBe(batchB.Id);
            batch.Quantity.ShouldBe(5);
        }
    }

    [Theory]
    [InlineData("/healthz")]
    [InlineData("/readyz")]
    public async Task HealthEndpoints_Return200WhenDatabaseIsReachable(string path)
    {
        var response = await _client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe(
            "Healthy"
        );
    }
}
