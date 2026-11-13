using System.Net.Http.Json;
using CustomerService.DataAccess;
using CustomerService.Dtos;
using CustomerService.EventProcessing;
using CustomerService.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute.ExceptionExtensions;
using Sava.Tests.Shared;

namespace CustomerService.Tests;

// A message that fails is given up after two attempts.
public class InboxApiFactory(MySqlFixture mySql) : CustomerApiFactory(mySql)
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

    private async Task<Guid> CreateCustomerAsync() =>
        (
            await (
                await _client.PostAsJsonAsync("/api/customers", CustomerBuilder.CreateDto(), Ct)
            ).Content.ReadFromJsonAsync<CustomerReadDto>(Ct)
        )
            .ShouldNotBeNull()
            .Id;

    private async Task<Guid?> OrderCustomerAsync(Guid orderId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerContext>();
        return await db
            .CustomerOrders.Where(o => o.OrderId == orderId)
            .Select(o => (Guid?)o.CustomerId)
            .SingleOrDefaultAsync(Ct);
    }

    private async Task<InboxMessage> MessageAsync(Guid id)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerContext>();
        return await db.InboxMessages.AsNoTracking().SingleAsync(m => m.Id == id, Ct);
    }

    [Fact]
    public async Task Delivery_AppliesTheEventAndRecordsItCompleted()
    {
        var customerId = await CreateCustomerAsync();
        var orderId = TestData.Faker.Random.Guid();
        var messageId = TestData.Faker.Random.Guid();
        var message = Inbox.OrderEvent(
            messageId,
            EventProcessor.OrderPublished,
            orderId,
            customerId
        );

        (await factory.HandleAsync(message)).ShouldBe([DeliveryOutcome.Ack]);

        (await OrderCustomerAsync(orderId)).ShouldBe(customerId);
        var recorded = await MessageAsync(messageId);
        recorded.Status.ShouldBe(InboxStatus.Completed);
        recorded.AggregateId.ShouldBe(orderId);
        recorded.Payload.ShouldBe(message);
        recorded.Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task Redelivery_IsAcknowledgedWithoutProcessingAgain()
    {
        var customerId = await CreateCustomerAsync();
        var messageId = TestData.Faker.Random.Guid();
        var message = Inbox.OrderEvent(
            messageId,
            EventProcessor.OrderPublished,
            TestData.Faker.Random.Guid(),
            customerId
        );

        (await factory.HandleAsync(message, message)).ShouldBe([
            DeliveryOutcome.Ack,
            DeliveryOutcome.Ack,
        ]);

        (await MessageAsync(messageId)).Attempts.ShouldBe(1);
    }

    [Fact]
    public async Task InvalidEvent_IsRecordedFailedAndAcknowledged()
    {
        var orderId = TestData.Faker.Random.Guid();
        var messageId = TestData.Faker.Random.Guid();

        (
            await factory.HandleAsync(
                Inbox.OrderEvent(messageId, EventProcessor.OrderPublished, orderId, null)
            )
        ).ShouldBe([DeliveryOutcome.Ack]);

        var failed = await MessageAsync(messageId);
        failed.Status.ShouldBe(InboxStatus.Failed);
        failed.Attempts.ShouldBe(1);
        failed.LastError.ShouldBe("Order_Published has no CustomerId.");
        (await OrderCustomerAsync(orderId)).ShouldBeNull();
    }

    [Fact]
    public async Task Cleanup_DeletesOldCompletedMessagesAndKeepsFailedOnes()
    {
        var now = DateTime.UtcNow;
        InboxMessage Message(InboxStatus status, DateTime createdAt) =>
            new()
            {
                Id = TestData.Faker.Random.Guid(),
                Type = EventProcessor.OrderPublished,
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
            var db = scope.ServiceProvider.GetRequiredService<CustomerContext>();
            db.InboxMessages.AddRange(oldCompleted, oldFailed, recent);
            await db.SaveChangesAsync(Ct);
        }

        await ActivatorUtilities.CreateInstance<InboxCleanup>(factory.Services).CleanupAsync(Ct);

        using var assert = factory.Services.CreateScope();
        var ids = await assert
            .ServiceProvider.GetRequiredService<CustomerContext>()
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
    [Fact]
    public async Task FailedProcessing_IsRetriedThenGivenUpAfterMaxAttempts()
    {
        var error = TestData.Faker.Lorem.Sentence();
        factory
            .Processor.ProcessEvent(Arg.Any<string>())
            .ThrowsAsync(new InvalidOperationException(error));
        var messageId = TestData.Faker.Random.Guid();
        var message = Inbox.OrderEvent(
            messageId,
            EventProcessor.OrderPublished,
            TestData.Faker.Random.Guid(),
            TestData.Faker.Random.Guid()
        );

        (await factory.HandleAsync(message)).ShouldBe([DeliveryOutcome.Retry]);
        (await factory.HandleAsync(message)).ShouldBe([DeliveryOutcome.Ack]);

        using var scope = factory.Services.CreateScope();
        var failed = await scope
            .ServiceProvider.GetRequiredService<CustomerContext>()
            .InboxMessages.AsNoTracking()
            .SingleAsync(m => m.Id == messageId, TestContext.Current.CancellationToken);
        failed.Status.ShouldBe(InboxStatus.Failed);
        failed.Attempts.ShouldBe(2);
        failed.LastError.ShouldBe(error);
    }
}
