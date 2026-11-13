using System.Text.Json;
using CustomerService.DataAccess;
using CustomerService.Dtos;
using CustomerService.EventProcessing;
using CustomerService.Messaging;
using Sava.Tests.Shared;

namespace CustomerService.Tests;

public sealed class EventProcessorTests
{
    private readonly ICustomerRepo _repo = Substitute.For<ICustomerRepo>();
    private readonly EventProcessor _processor;

    public EventProcessorTests()
    {
        _processor = new EventProcessor(_repo);
    }

    private async Task ShouldBeRejectedAsync(string message, string reason)
    {
        (
            await Should.ThrowAsync<InvalidEventException>(() => _processor.ProcessEvent(message))
        ).Message.ShouldBe(reason);
        _repo.ReceivedCalls().ShouldBeEmpty();
    }

    private static string Message(string eventName, Guid? orderId, Guid? customerId) =>
        JsonSerializer.Serialize(new OrderPublishedDto(orderId, customerId, eventName));

    [Fact]
    public async Task OrderPublished_RecordsTheOrdersCustomer()
    {
        var order = TestData.Faker.Random.Guid();
        var customer = TestData.Faker.Random.Guid();

        await _processor.ProcessEvent(Message(EventProcessor.OrderPublished, order, customer));

        await _repo.Received(1).SetOrderCustomerAsync(order, customer);
    }

    [Fact]
    public async Task OrderDeleted_RemovesTheReference()
    {
        var order = TestData.Faker.Random.Guid();

        await _processor.ProcessEvent(
            Message(EventProcessor.OrderDeleted, order, TestData.Faker.Random.Guid())
        );

        await _repo.Received(1).RemoveOrderAsync(order);
    }

    [Theory]
    [InlineData(true, false, "Order_Published has no CustomerId.")]
    [InlineData(false, true, "Order_Published has no OrderId.")]
    public async Task OrderPublished_MissingIds_IsRejected(
        bool hasOrderId,
        bool hasCustomerId,
        string reason
    ) =>
        await ShouldBeRejectedAsync(
            Message(
                EventProcessor.OrderPublished,
                hasOrderId ? TestData.Faker.Random.Guid() : null,
                hasCustomerId ? TestData.Faker.Random.Guid() : Guid.Empty
            ),
            reason
        );

    [Theory]
    [InlineData("{\"Event\":\"Item_Published\"}", "Item_Published")]
    [InlineData("{\"Event\":\"\"}", "")]
    [InlineData("null", "")]
    public async Task UnrelatedMessages_AreRejected(string message, string eventName) =>
        await ShouldBeRejectedAsync(message, $"Unknown event '{eventName}'.");
}
