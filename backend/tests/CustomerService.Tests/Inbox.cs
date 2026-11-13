using System.Text.Json;
using CustomerService.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Sava.Tests.Shared;

namespace CustomerService.Tests;

// Order events as OrderService sends them, and their delivery the way the subscriber and the
// inbox job handle them.
internal static class Inbox
{
    public static string OrderEvent(string eventName, Guid? orderId, Guid? customerId) =>
        OrderEvent(TestData.Faker.Random.Guid(), eventName, orderId, customerId);

    public static string OrderEvent(
        Guid messageId,
        string eventName,
        Guid? orderId,
        Guid? customerId
    ) =>
        JsonSerializer.Serialize(
            new
            {
                MessageId = messageId,
                OrderId = orderId,
                CustomerId = customerId,
                Items = Array.Empty<object>(),
                Event = eventName,
            }
        );

    // Hands the messages to the inbox, as MessageBusSubscriber does on delivery, and returns what
    // the subscriber would do with each.
    public static async Task<List<DeliveryOutcome>> HandleAsync(
        this CustomerApiFactory factory,
        params string[] messages
    )
    {
        var inbox = factory.Services.GetRequiredService<IInboxHandler>();
        var outcomes = new List<DeliveryOutcome>();
        foreach (var message in messages)
        {
            outcomes.Add(
                await inbox.HandleAsync(message, null, TestContext.Current.CancellationToken)
            );
        }
        return outcomes;
    }

    // Delivers the messages and expects each to be done with (acknowledged).
    public static async Task DeliverAsync(
        this CustomerApiFactory factory,
        params string[] messages
    ) => (await factory.HandleAsync(messages)).ShouldAllBe(o => o == DeliveryOutcome.Ack);
}
