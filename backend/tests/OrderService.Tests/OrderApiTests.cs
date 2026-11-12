using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute.ExceptionExtensions;
using OrderService.Dtos;
using OrderService.Mappings;
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

    private static async Task<ProblemDetails> ShouldBeProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status
    )
    {
        response.StatusCode.ShouldBe(status);
        return (await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct)).ShouldNotBeNull();
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
        await factory
            .MessageBus.Received(1)
            .PublishNewOrderAsync(
                Arg.Is<OrderPublishedEvent>(e =>
                    e.Event == "Order_Published"
                    && e.OrderId == order.Id
                    && e.CustomerId == factory.CustomerId
                    && e.Items.Single().ProductId == product.Id
                    && e.Items.Single().Batches.Sum(b => b.Quantity) == q0 + q1
                )
            );
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
                OrderBuilder.UpdateDto(
                    OrderBuilder.Status(),
                    null,
                    OrderBuilder.ItemDto(factory.Products[0].Id, (unknownBatch, 1))
                ),
                Ct
            ),
            HttpStatusCode.BadRequest
        );

        problem.Detail.ShouldBe($"Batch {unknownBatch} does not exist.");
        var saved = await GetOrderAsync(order.Id);
        saved.OrderStatus.ShouldBe(order.OrderStatus);
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
                OrderBuilder.UpdateDto(OrderBuilder.Status()),
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
    public async Task OrderAndInvoiceLifecycle()
    {
        var creator = TestData.Faker.Internet.UserName();
        var editor = TestData.Faker.Internet.UserName();
        _client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(
            TestTokens.Bearer(creator)
        );
        var order = await CreateOrderAsync();
        order.Audit.ShouldNotBeNull().CreatedBy.ShouldBe(creator);
        order.Audit.UpdatedBy.ShouldBeNull();
        var productB = factory.Products[1];
        var batchB = factory.Batches[2];

        _client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(
            TestTokens.Bearer(editor)
        );
        var update = await _client.PutAsJsonAsync(
            $"/api/orders/{order.Id}",
            OrderBuilder.UpdateDto("Confirmed", null, ItemDto(1, 5)),
            Ct
        );
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = await GetOrderAsync(order.Id);
        updated.OrderStatus.ShouldBe("Confirmed");
        updated.ShippingAddress.ShouldBe(order.ShippingAddress);
        var item = updated.Items.ShouldHaveSingleItem();
        item.ProductId.ShouldBe(productB.Id);
        item.Name.ShouldBe(productB.Name);
        item.Batches.ShouldHaveSingleItem().BatchId.ShouldBe(batchB.Id);
        updated.Audit.ShouldNotBeNull().CreatedBy.ShouldBe(creator);
        updated.Audit.UpdatedBy.ShouldBe(editor);
        updated.Audit.UpdatedAt.ShouldNotBeNull();

        var invoiceDto = new InvoiceCreateDto(
            null,
            TestData.Date(2025, 2026),
            "Draft",
            updated.Totals.Gross
        );
        var createInvoice = await _client.PostAsJsonAsync(
            $"/api/invoices/{order.Id}",
            invoiceDto,
            Ct
        );
        createInvoice.StatusCode.ShouldBe(HttpStatusCode.Created);
        var invoice = (
            await createInvoice.Content.ReadFromJsonAsync<InvoiceReadDto>(Ct)
        ).ShouldNotBeNull();
        invoice.OrderId.ShouldBe(order.Id);
        invoice.Audit.CreatedBy.ShouldBe(editor);
        (
            await _client.PostAsJsonAsync($"/api/invoices/{order.Id}", invoiceDto, Ct)
        ).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var paid = await _client.PutAsJsonAsync(
            $"/api/invoices/{invoice.Id}",
            new InvoiceUpdateDto(null, "Paid", null),
            Ct
        );
        paid.StatusCode.ShouldBe(HttpStatusCode.OK);
        var paidInvoice = (
            await paid.Content.ReadFromJsonAsync<InvoiceReadDto>(Ct)
        ).ShouldNotBeNull();
        paidInvoice.InvoiceStatus.ShouldBe("Paid");
        paidInvoice.TotalAmount.ShouldBe(updated.Totals.Gross);
        paidInvoice.OrderId.ShouldBe(order.Id);
        paidInvoice.Audit.UpdatedBy.ShouldBe(editor);
        _client.DefaultRequestHeaders.Authorization = null;
        var invoiced = (
            await _client.GetFromJsonAsync<OrderReadDto>($"/api/invoices/{invoice.Id}", Ct)
        ).ShouldNotBeNull();
        invoiced.Id.ShouldBe(order.Id);
        invoiced.Invoice.ShouldNotBeNull().InvoiceStatus.ShouldBe("Paid");
        invoiced.Items.ShouldHaveSingleItem().ProductId.ShouldBe(productB.Id);
        invoiced.Customer.ShouldBe(factory.Customer.ToReadDto());
        var invoices = await _client.GetFromJsonAsync<InvoicePageReadDto>("/api/invoices", Ct);
        invoices
            .ShouldNotBeNull()
            .OrderReadDtos.ShouldNotBeNull()
            .ShouldContain(o => o.Id == order.Id);

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

        (await _client.DeleteAsync($"/api/invoices/{invoice.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );
        (await _client.DeleteAsync($"/api/orders/{order.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NoContent
        );
        (await _client.GetAsync($"/api/orders/{order.Id}", Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );
        await factory
            .MessageBus.Received(1)
            .PublishNewOrderAsync(
                Arg.Is<OrderPublishedEvent>(e =>
                    e.Event == "Order_Deleted"
                    && e.OrderId == order.Id
                    && e.Items.Single().ProductId == productB.Id
                    && e.Items.Single().Batches.Single().BatchId == batchB.Id
                )
            );
        // The update published the order's complete new item list.
        await factory
            .MessageBus.Received(1)
            .PublishNewOrderAsync(
                Arg.Is<OrderPublishedEvent>(e =>
                    e.Event == "Order_Published"
                    && e.OrderId == order.Id
                    && e.Items.Single().ProductId == productB.Id
                    && e.Items.Single().Batches.Single().BatchId == batchB.Id
                    && e.Items.Single().Batches.Single().Quantity == 5
                )
            );
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
