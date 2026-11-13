using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ExceptionExtensions;
using OrderService.AsyncDataServices;
using OrderService.DataAccess;
using OrderService.Dtos;
using OrderService.Messaging;
using OrderService.Models;
using Sava.Tests.Shared;

namespace OrderService.Tests;

public sealed class OutboxPublisherTests(MySqlFixture mySql) : OrderDbTestBase(mySql), IDisposable
{
    private readonly IMessageBusClient _bus = Substitute.For<IMessageBusClient>();
    private readonly FakeTimeProvider _clock = new(
        new DateTimeOffset(TestData.Date(2026, 2027), TimeSpan.Zero)
    );
    private readonly MessagingOptions _options = new()
    {
        MaxAttempts = 2,
        RetryBaseDelaySeconds = 30,
        RetryMaxDelaySeconds = 300,
        RetentionDays = 7,
    };
    private ServiceProvider? _services;

    public void Dispose() => _services?.Dispose();

    private OutboxPublisher Publisher()
    {
        _services ??= new ServiceCollection()
            .AddScoped(_ => Db.CreateContext())
            .BuildServiceProvider();
        return new OutboxPublisher(
            _services.GetRequiredService<IServiceScopeFactory>(),
            _bus,
            new OutboxSignal(),
            _clock,
            Options.Create(_options)
        );
    }

    private async Task<Order> SaveOrderAsync(OrderEvent orderEvent, Order? order = null)
    {
        order ??= OrderBuilder.Order(OrderBuilder.Item());
        order.Raise(orderEvent);
        await using var context = Db.CreateContext();
        if (await context.Orders.AnyAsync(o => o.Id == order.Id, Ct))
        {
            context.Orders.Update(order);
        }
        else
        {
            context.Orders.Add(order);
        }
        await context.SaveChangesAsync(Ct);
        return order;
    }

    private async Task<List<OutboxMessage>> MessagesAsync()
    {
        await using var context = Db.CreateContext();
        return await context.OutboxMessages.OrderBy(m => m.Sequence).ToListAsync(Ct);
    }

    [Fact]
    public async Task PublishDue_SendsPendingMessagesInOrderAndMarksThemSent()
    {
        var first = await SaveOrderAsync(new OrderPublished());
        var second = await SaveOrderAsync(new OrderDeleted());

        (await Publisher().PublishDueAsync(Ct)).ShouldBeTrue();

        var messages = await MessagesAsync();
        messages.Select(m => m.AggregateId).ShouldBe([first.Id, second.Id]);
        Received.InOrder(() =>
        {
            _bus.PublishAsync(
                messages[0].Id,
                "Order_Published",
                messages[0].Payload!,
                Arg.Any<CancellationToken>()
            );
            _bus.PublishAsync(
                messages[1].Id,
                "Order_Deleted",
                messages[1].Payload!,
                Arg.Any<CancellationToken>()
            );
        });
        messages.ShouldAllBe(m =>
            m.Status == OutboxStatus.Sent && m.Attempts == 1 && m.ProcessedAt != null
        );
    }

    [Fact]
    public async Task PublishDue_SendsOnlyTheNewestUnsentMessageOfAnOrder()
    {
        var order = await SaveOrderAsync(new OrderPublished());
        await SaveOrderAsync(new OrderDeleted(), order);
        await SaveOrderAsync(new OrderPublished(), order);

        await Publisher().PublishDueAsync(Ct);

        var messages = await MessagesAsync();
        messages
            .Select(m => m.Status)
            .ShouldBe([OutboxStatus.Superseded, OutboxStatus.Superseded, OutboxStatus.Sent]);
        await _bus.Received(1)
            .PublishAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await _bus.Received(1)
            .PublishAsync(
                messages[2].Id,
                "Order_Published",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task FailedSend_IsRetriedAfterTheBackoffAndGivenUpAfterMaxAttempts()
    {
        await SaveOrderAsync(new OrderPublished());
        var error = TestData.Faker.Lorem.Sentence();
        _bus.PublishAsync(default, default!, default!, default)
            .ThrowsAsyncForAnyArgs(new InvalidOperationException(error));
        var publisher = Publisher();

        await publisher.PublishDueAsync(Ct);

        var retrying = (await MessagesAsync()).ShouldHaveSingleItem();
        retrying.Status.ShouldBe(OutboxStatus.Pending);
        retrying.Attempts.ShouldBe(1);
        retrying.LastError.ShouldBe(error);
        retrying.NextAttemptAt.ShouldBe(_clock.GetUtcNow().UtcDateTime.AddSeconds(30));

        // Not due yet: nothing is sent.
        await publisher.PublishDueAsync(Ct);
        (await MessagesAsync()).ShouldHaveSingleItem().Attempts.ShouldBe(1);

        _clock.Advance(TimeSpan.FromSeconds(30));
        await publisher.PublishDueAsync(Ct);

        var failed = (await MessagesAsync()).ShouldHaveSingleItem();
        failed.Status.ShouldBe(OutboxStatus.Failed);
        failed.Attempts.ShouldBe(2);
        failed.NextAttemptAt.ShouldBeNull();
    }

    [Fact]
    public async Task BrokerUnavailable_LeavesMessagesPendingWithoutUsingAttempts()
    {
        await SaveOrderAsync(new OrderPublished());
        _bus.PublishAsync(default, default!, default!, default)
            .ThrowsAsyncForAnyArgs(new MessageBusUnavailableException(new IOException()));
        var publisher = Publisher();

        await publisher.PublishDueAsync(Ct);

        var pending = (await MessagesAsync()).ShouldHaveSingleItem();
        pending.Status.ShouldBe(OutboxStatus.Pending);
        pending.Attempts.ShouldBe(0);

        _bus.ClearReceivedCalls();
        _bus.PublishAsync(default, default!, default!, default)
            .ReturnsForAnyArgs(Task.CompletedTask);
        await publisher.PublishDueAsync(Ct);
        (await MessagesAsync()).ShouldHaveSingleItem().Status.ShouldBe(OutboxStatus.Sent);
    }

    [Fact]
    public async Task PublishDue_WhileAnotherPodHoldsTheLock_SendsNothing()
    {
        await SaveOrderAsync(new OrderPublished());
        await using var other = Db.CreateContext();
        await other.Database.OpenConnectionAsync(Ct);
        (
            await other
                .Database.SqlQueryRaw<int?>(
                    "SELECT GET_LOCK(CONCAT('outbox:', DATABASE()), 0) AS Value"
                )
                .SingleAsync(Ct)
        ).ShouldBe(1);

        (await Publisher().PublishDueAsync(Ct)).ShouldBeFalse();

        _bus.ReceivedCalls().ShouldBeEmpty();
        (await MessagesAsync()).ShouldHaveSingleItem().Status.ShouldBe(OutboxStatus.Pending);
    }

    [Fact]
    public async Task Cleanup_DeletesOldSentAndSupersededMessagesAndKeepsFailedOnes()
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        OutboxMessage Message(OutboxStatus status, DateTime createdAt) =>
            new()
            {
                Id = TestData.Faker.Random.Guid(),
                Type = "Order_Published",
                AggregateId = TestData.Faker.Random.Guid(),
                Payload = "{}",
                Status = status,
                CreatedAt = createdAt,
            };
        var oldSent = Message(OutboxStatus.Sent, now.AddDays(-8));
        var oldSuperseded = Message(OutboxStatus.Superseded, now.AddDays(-8));
        var oldFailed = Message(OutboxStatus.Failed, now.AddDays(-8));
        var oldPending = Message(OutboxStatus.Pending, now.AddDays(-8));
        var recentSent = Message(OutboxStatus.Sent, now.AddDays(-6));
        await SeedAsync(oldSent, oldSuperseded, oldFailed, oldPending, recentSent);

        Publisher();
        var cleanup = new OutboxCleanup(
            _services!.GetRequiredService<IServiceScopeFactory>(),
            _clock,
            Options.Create(_options)
        );
        (await cleanup.CleanupAsync(Ct)).ShouldBe(2);

        (await MessagesAsync())
            .Select(m => m.Id)
            .ShouldBe([oldFailed.Id, oldPending.Id, recentSent.Id], ignoreOrder: true);
    }

    [Fact]
    public async Task SavingAnOrderWithAnEvent_WakesThePublisher()
    {
        var signal = new OutboxSignal();
        var order = OrderBuilder.Order(OrderBuilder.Item());
        order.Raise(new OrderPublished());
        await using (var context = new OrderContext(Db.Options, outboxSignal: signal))
        {
            context.Orders.Add(order);
            await context.SaveChangesAsync(Ct);
        }

        var woken = signal.WaitAsync(TimeSpan.FromSeconds(30), Ct);
        (await Task.WhenAny(woken, Task.Delay(TimeSpan.FromSeconds(5), Ct))).ShouldBe(woken);
    }
}

// The app with its outbox publisher running.
public class PublishingOrderApiFactory(MySqlFixture mySql) : OrderApiFactory(mySql)
{
    protected override bool RunsOutboxPublisher => true;
}

[Trait("Category", "Integration")]
public class OutboxPublisherApiTests(PublishingOrderApiFactory factory)
    : IClassFixture<PublishingOrderApiFactory>
{
    [Fact]
    public async Task CreatingAnOrder_SendsItsEventRightAway()
    {
        var ct = TestContext.Current.CancellationToken;
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/orders",
            OrderBuilder.CreateDto(
                factory.CustomerId,
                OrderBuilder.ItemDto(factory.Products[0].Id, (factory.Batches[0].Id, 1))
            ),
            ct
        );
        var order = (await response.Content.ReadFromJsonAsync<OrderReadDto>(ct)).ShouldNotBeNull();

        // Long before the sweep: the save wakes the publisher.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (
            !factory
                .MessageBus.ReceivedCalls()
                .Any(c => ((string)c.GetArguments()[2]!).Contains(order.Id.ToString()))
        )
        {
            DateTime.UtcNow.ShouldBeLessThan(deadline, "the event was not sent");
            await Task.Delay(50, ct);
        }
        var sent = (await factory.OutboxFor(order.Id)).ShouldHaveSingleItem();
        sent.Event.ShouldBe("Order_Published");
    }
}

public class NightlyTests
{
    [Theory]
    [InlineData("2026-11-13T00:30:00", 1, "2026-11-13T01:00:00")]
    [InlineData("2026-11-13T01:00:00", 1, "2026-11-14T01:00:00")]
    [InlineData("2026-11-13T23:59:00", 1, "2026-11-14T01:00:00")]
    [InlineData("2026-11-13T05:00:00", 0, "2026-11-14T00:00:00")]
    public void NextRun_IsTheNextOccurrenceOfTheHour(string now, int hour, string next) =>
        Nightly.NextRun(DateTime.Parse(now), hour).ShouldBe(DateTime.Parse(next));
}
