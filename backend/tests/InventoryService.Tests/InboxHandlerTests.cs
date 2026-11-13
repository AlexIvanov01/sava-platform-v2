using System.Net.Http.Json;
using InventoryService.DataAccess;
using InventoryService.Dtos;
using InventoryService.EventProcessing;
using InventoryService.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute.ExceptionExtensions;
using Sava.Tests.Shared;

namespace InventoryService.Tests;

// A message that fails is given up after two attempts.
public class InboxApiFactory(MySqlFixture mySql) : InventoryApiFactory(mySql)
{
    protected override IReadOnlyDictionary<string, string> Settings { get; } =
        new Dictionary<string, string> { ["Messaging:MaxAttempts"] = "2" };
}

// The inbox as the subscriber drives it: every delivery is recorded and processed in one go.
[Trait("Category", "Integration")]
public class InboxHandlerTests(InboxApiFactory factory) : IClassFixture<InboxApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<(Guid ProductId, Guid BatchId)> CreateBatchAsync()
    {
        var product = (
            await (
                await _client.PostAsJsonAsync(
                    "/api/products",
                    InventoryBuilder.ProductCreateDto(),
                    Ct
                )
            ).Content.ReadFromJsonAsync<ProductReadDto>(Ct)
        ).ShouldNotBeNull();
        var batch = (
            await (
                await _client.PostAsJsonAsync(
                    $"/api/batches/{product.Id}",
                    InventoryBuilder.BatchCreateDto(quantity: 50),
                    Ct
                )
            ).Content.ReadFromJsonAsync<ProductReadDto>(Ct)
        )
            .ShouldNotBeNull()
            .Batches.Single();
        return (product.Id, batch.Id);
    }

    private async Task<List<BatchOrderReadDto>> HoldingsAsync(Guid batchId) =>
        (
            await _client.GetFromJsonAsync<List<BatchOrderReadDto>>(
                $"/api/batches/{batchId}/orders",
                Ct
            )
        ).ShouldNotBeNull();

    private async Task<InboxMessage> MessageAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductContext>();
        return await db.InboxMessages.AsNoTracking().SingleAsync(m => m.Id == id, Ct);
    }

    [Fact]
    public async Task Delivery_AppliesTheEventAndRecordsItCompleted()
    {
        var (productId, batchId) = await CreateBatchAsync();
        var orderId = TestData.Faker.Random.Guid();
        var messageId = TestData.Faker.Random.Guid();
        var message = Inbox.OrderEvent(
            messageId,
            "Order_Published",
            orderId,
            [new OrderItemPublishedDto(productId, [new OrderItemBatchPublishedDto(batchId, 4)])]
        );

        (await factory.HandleAsync(message)).ShouldBe([DeliveryOutcome.Ack]);

        (await HoldingsAsync(batchId)).ShouldBe([new BatchOrderReadDto(orderId, batchId, 4)]);
        var recorded = await MessageAsync(messageId);
        recorded.Status.ShouldBe(InboxStatus.Completed);
        recorded.Type.ShouldBe("Order_Published");
        recorded.AggregateId.ShouldBe(orderId);
        recorded.Payload.ShouldBe(message);
        recorded.Attempts.ShouldBe(1);
        recorded.ProcessedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Redelivery_IsAcknowledgedWithoutProcessingAgain()
    {
        var (productId, batchId) = await CreateBatchAsync();
        var orderId = TestData.Faker.Random.Guid();
        var messageId = TestData.Faker.Random.Guid();
        var message = Inbox.OrderEvent(
            messageId,
            "Order_Published",
            orderId,
            [new OrderItemPublishedDto(productId, [new OrderItemBatchPublishedDto(batchId, 3)])]
        );

        (await factory.HandleAsync(message, message)).ShouldBe([
            DeliveryOutcome.Ack,
            DeliveryOutcome.Ack,
        ]);

        (await MessageAsync(messageId)).Attempts.ShouldBe(1);
        var movements = await _client.GetFromJsonAsync<List<StockMovementReadDto>>(
            $"/api/batches/{batchId}/movements",
            Ct
        );
        movements.ShouldNotBeNull().Where(m => m.OrderId == orderId).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task InvalidEvent_IsRecordedFailedAndAcknowledged()
    {
        var messageId = TestData.Faker.Random.Guid();

        (
            await factory.HandleAsync(
                Inbox.OrderEvent(messageId, "Order_Published", TestData.Faker.Random.Guid(), [])
            )
        ).ShouldBe([DeliveryOutcome.Ack]);

        var failed = await MessageAsync(messageId);
        failed.Status.ShouldBe(InboxStatus.Failed);
        failed.Attempts.ShouldBe(1);
        failed.LastError.ShouldBe("Order_Published has no items.");
    }

    [Fact]
    public async Task UnusableMessage_IsRecordedFailedAndAcknowledged()
    {
        var messageId = TestData.Faker.Random.Guid();

        (
            await factory.HandleAsync(
                Inbox.OrderEvent(messageId, "Order_Shipped", TestData.Faker.Random.Guid(), [])
            )
        ).ShouldBe([DeliveryOutcome.Ack]);

        var failed = await MessageAsync(messageId);
        failed.Status.ShouldBe(InboxStatus.Failed);
        failed.AggregateId.ShouldBeNull();
        failed.Attempts.ShouldBe(0);
        failed.LastError.ShouldBe("Unknown event 'Order_Shipped'.");
    }

    [Fact]
    public async Task Cleanup_DeletesOldCompletedMessagesAndKeepsFailedOnes()
    {
        var now = DateTime.UtcNow;
        InboxMessage Message(InboxStatus status, DateTime createdAt) =>
            new()
            {
                Id = TestData.Faker.Random.Guid(),
                Type = "Order_Published",
                AggregateId = TestData.Faker.Random.Guid(),
                Payload = "{}",
                Status = status,
                CreatedAt = createdAt,
            };
        var oldCompleted = Message(InboxStatus.Completed, now.AddDays(-8));
        var oldFailed = Message(InboxStatus.Failed, now.AddDays(-8));
        var recent = Message(InboxStatus.Completed, now.AddDays(-6));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProductContext>();
            db.InboxMessages.AddRange(oldCompleted, oldFailed, recent);
            await db.SaveChangesAsync(Ct);
        }

        await ActivatorUtilities.CreateInstance<InboxCleanup>(factory.Services).CleanupAsync(Ct);

        using var assert = factory.Services.CreateScope();
        var ids = await assert
            .ServiceProvider.GetRequiredService<ProductContext>()
            .InboxMessages.Select(m => m.Id)
            .ToListAsync(Ct);
        ids.ShouldNotContain(oldCompleted.Id);
        ids.ShouldContain(oldFailed.Id);
        ids.ShouldContain(recent.Id);
    }
}

// The processor fails with a transient error.
public class FailingInboxApiFactory(MySqlFixture mySql) : InboxApiFactory(mySql)
{
    public IEventProcessor Processor { get; } = Substitute.For<IEventProcessor>();

    protected override void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IEventProcessor>();
        services.AddScoped(_ => Processor);
    }
}

[Trait("Category", "Integration")]
public class InboxRetryTests(FailingInboxApiFactory factory) : IClassFixture<FailingInboxApiFactory>
{
    private async Task<InboxMessage> MessageAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductContext>();
        return await db
            .InboxMessages.AsNoTracking()
            .SingleAsync(m => m.Id == id, TestContext.Current.CancellationToken);
    }

    private static string Message(Guid messageId) =>
        Inbox.OrderEvent(
            messageId,
            "Order_Published",
            TestData.Faker.Random.Guid(),
            [new OrderItemPublishedDto(TestData.Faker.Random.Guid(), [])]
        );

    [Fact]
    public async Task FailedProcessing_IsRetriedThenGivenUpAfterMaxAttempts()
    {
        var error = TestData.Faker.Lorem.Sentence();
        factory
            .Processor.ProcessEvent(Arg.Any<string>())
            .ThrowsAsync(new InvalidOperationException(error));
        var messageId = TestData.Faker.Random.Guid();

        (await factory.HandleAsync(Message(messageId))).ShouldBe([DeliveryOutcome.Retry]);

        var retrying = await MessageAsync(messageId);
        retrying.Status.ShouldBe(InboxStatus.Received);
        retrying.Attempts.ShouldBe(1);
        retrying.LastError.ShouldBe(error);

        (await factory.HandleAsync(Message(messageId))).ShouldBe([DeliveryOutcome.Ack]);

        var failed = await MessageAsync(messageId);
        failed.Status.ShouldBe(InboxStatus.Failed);
        failed.Attempts.ShouldBe(2);
    }

    [Fact]
    public async Task MessageThatFailedOnce_IsCompletedWhenTheRetrySucceeds()
    {
        var messageId = TestData.Faker.Random.Guid();
        var message = Message(messageId);
        var calls = 0;
        factory
            .Processor.ProcessEvent(message)
            .Returns(_ =>
                ++calls == 1
                    ? Task.FromException(new InvalidOperationException())
                    : Task.CompletedTask
            );

        (await factory.HandleAsync(message, message)).ShouldBe([
            DeliveryOutcome.Retry,
            DeliveryOutcome.Ack,
        ]);

        var completed = await MessageAsync(messageId);
        completed.Status.ShouldBe(InboxStatus.Completed);
        completed.Attempts.ShouldBe(2);
        completed.LastError.ShouldBeNull();
    }
}
