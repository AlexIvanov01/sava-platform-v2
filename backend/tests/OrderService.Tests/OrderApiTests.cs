using System.Net;
using System.Net.Http.Json;
using OrderService.Dtos;

namespace OrderService.Tests;

[Trait("Category", "Integration")]
public class OrderApiTests(OrderApiFactory factory) : IClassFixture<OrderApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<OrderFullReadDto> CreateOrderAsync(int quantity = 2)
    {
        var dto = OrderBuilder.CreateDto(
            factory.Customer.ExternalId,
            (factory.Items[0].ExternalBatchId, quantity)
        );
        var response = await _client.PostAsJsonAsync("/api/orders", dto, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<OrderFullReadDto>(Ct)).ShouldNotBeNull();
    }

    [Fact]
    public async Task CreateOrder_UsesItemsAndCustomerSyncedAtStartup()
    {
        var order = await CreateOrderAsync(2);

        order.Customer.ShouldNotBeNull().ExternalId.ShouldBe(factory.Customer.ExternalId);
        var line = order.Items.ShouldHaveSingleItem();
        line.Name.ShouldBe("Synced A");
        line.Quantity.ShouldBe(2);
        await factory
            .MessageBus.Received()
            .PublishNewOrderAsync(
                Arg.Is<OrderPublishedEvent>(e =>
                    e.Event == "Order_Published"
                    && e.OrderItems.Single().ItemId == factory.Items[0].ExternalBatchId
                )
            );
    }

    [Fact]
    public async Task CreateOrder_UnknownItem_Returns404()
    {
        var dto = OrderBuilder.CreateDto(factory.Customer.ExternalId, (Guid.NewGuid(), 1));

        (await _client.PostAsJsonAsync("/api/orders", dto, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.NotFound
        );
    }

    [Fact]
    public async Task CreateOrder_InvalidQuantity_Returns400()
    {
        var dto = OrderBuilder.CreateDto(
            factory.Customer.ExternalId,
            (factory.Items[0].ExternalBatchId, 0)
        );

        (await _client.PostAsJsonAsync("/api/orders", dto, Ct)).StatusCode.ShouldBe(
            HttpStatusCode.BadRequest
        );
    }

    [Fact]
    public async Task CreateOrder_DuplicateItems_Returns400()
    {
        var itemId = factory.Items[0].ExternalBatchId;
        var dto = OrderBuilder.CreateDto(factory.Customer.ExternalId, (itemId, 1), (itemId, 2));

        var response = await _client.PostAsJsonAsync("/api/orders", dto, Ct);

        await ShouldBeDuplicateItemsProblemAsync(response, itemId);
    }

    [Fact]
    public async Task UpdateOrder_DuplicateItems_Returns400AndKeepsOrder()
    {
        var order = await CreateOrderAsync();
        var itemId = factory.Items[1].ExternalBatchId;

        var response = await _client.PutAsJsonAsync(
            $"/api/orders/{order.Id}",
            OrderBuilder.UpdateDto(null, null, (itemId, 1), (itemId, 3)),
            Ct
        );

        await ShouldBeDuplicateItemsProblemAsync(response, itemId);
        (await _client.GetFromJsonAsync<OrderFullReadDto>($"/api/orders/{order.Id}", Ct))
            .ShouldNotBeNull()
            .Items.ShouldHaveSingleItem()
            .ExternalBatchId.ShouldBe(factory.Items[0].ExternalBatchId);
    }

    private static async Task ShouldBeDuplicateItemsProblemAsync(
        HttpResponseMessage response,
        Guid itemId
    )
    {
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = (
            await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(
                Ct
            )
        ).ShouldNotBeNull();
        problem.Errors.ShouldContainKey("OrderItems");
        problem.Errors["OrderItems"].ShouldHaveSingleItem().ShouldContain(itemId.ToString());
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
            $"/api/orders?customerId={factory.Customer.ExternalId}",
            Ct
        );

        page.ShouldNotBeNull().OrderFullReadDtos.ShouldNotBeEmpty();
        page.OrderFullReadDtos.ShouldAllBe(o =>
            o.Customer!.ExternalId == factory.Customer.ExternalId
        );
        (
            await _client.GetAsync($"/api/orders?customerId={Guid.NewGuid()}", Ct)
        ).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OrderAndInvoiceLifecycle()
    {
        var order = await CreateOrderAsync();
        var itemB = factory.Items[1].ExternalBatchId;

        var update = await _client.PutAsJsonAsync(
            $"/api/orders/{order.Id}",
            OrderBuilder.UpdateDto("Confirmed", null, (itemB, 5)),
            Ct
        );
        update.StatusCode.ShouldBe(HttpStatusCode.OK);
        var updated = (
            await _client.GetFromJsonAsync<OrderFullReadDto>($"/api/orders/{order.Id}", Ct)
        ).ShouldNotBeNull();
        updated.OrderStatus.ShouldBe("Confirmed");
        updated.Items.ShouldHaveSingleItem().ExternalBatchId.ShouldBe(itemB);

        var invoiceDto = new InvoiceCreateDto(
            null,
            new DateTime(2025, 3, 2, 0, 0, 0, DateTimeKind.Unspecified),
            "Draft",
            15m
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
        paidInvoice.TotalAmount.ShouldBe(15m);
        paidInvoice.OrderId.ShouldBe(order.Id);
        var invoiced = (
            await _client.GetFromJsonAsync<OrderFullReadDto>($"/api/invoices/{invoice.Id}", Ct)
        ).ShouldNotBeNull();
        invoiced.Id.ShouldBe(order.Id);
        invoiced.Invoice.ShouldNotBeNull().InvoiceStatus.ShouldBe("Paid");
        invoiced.Invoice.TotalAmount.ShouldBe(15m);
        var invoices = await _client.GetFromJsonAsync<InvoicePageReadDto>("/api/invoices", Ct);
        invoices
            .ShouldNotBeNull()
            .OrderReadDtos.ShouldNotBeNull()
            .ShouldContain(o => o.Id == order.Id);

        // An invoiced order cannot be deleted; the rejected attempt leaves it intact.
        var rejected = await _client.DeleteAsync($"/api/orders/{order.Id}", Ct);
        rejected.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = (
            await rejected.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>(Ct)
        ).ShouldNotBeNull();
        problem.Status.ShouldBe(409);
        problem.Title.ShouldBe("Order has an invoice");
        problem.Detail.ShouldBe(
            $"Order {order.Id} has an invoice. Delete the invoice before deleting the order."
        );
        (await _client.GetFromJsonAsync<OrderFullReadDto>($"/api/orders/{order.Id}", Ct))
            .ShouldNotBeNull()
            .Items.ShouldHaveSingleItem();

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
            .MessageBus.Received()
            .PublishNewOrderAsync(
                Arg.Is<OrderPublishedEvent>(e =>
                    e.Event == "Order_Deleted" && e.OrderItems.Single().ItemId == itemB
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
