using System.Text.Json;
using CustomerService.DataAccess;
using CustomerService.Dtos;
using CustomerService.EventProcessing;
using Microsoft.Extensions.DependencyInjection;
using Sava.Tests.Shared;

namespace CustomerService.Tests;

public sealed class EventProcessorTests : IDisposable
{
    private readonly ICustomerRepo _repo = Substitute.For<ICustomerRepo>();
    private readonly ServiceProvider _services;
    private readonly EventProcessor _processor;

    public EventProcessorTests()
    {
        _services = new ServiceCollection().AddScoped(_ => _repo).BuildServiceProvider();
        _processor = new EventProcessor(_services.GetRequiredService<IServiceScopeFactory>());
    }

    public void Dispose() => _services.Dispose();

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
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task OrderPublished_MissingIds_IsSkipped(bool hasOrderId, bool hasCustomerId)
    {
        await _processor.ProcessEvent(
            Message(
                EventProcessor.OrderPublished,
                hasOrderId ? TestData.Faker.Random.Guid() : null,
                hasCustomerId ? TestData.Faker.Random.Guid() : Guid.Empty
            )
        );

        _repo.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("{\"Event\":\"Item_Published\"}")]
    [InlineData("{\"Event\":\"\"}")]
    [InlineData("null")]
    public async Task UnrelatedMessages_AreIgnored(string message)
    {
        await _processor.ProcessEvent(message);

        _repo.ReceivedCalls().ShouldBeEmpty();
    }
}
