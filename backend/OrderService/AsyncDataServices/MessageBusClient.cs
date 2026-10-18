using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OrderService.Dtos;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;

namespace OrderService.AsyncDataServices;

public sealed class MessageBusClient : IMessageBusClient, IAsyncDisposable
{
    private const string Exchange = "order_exchange";
    private const string RoutingKey = "order.published";

    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public MessageBusClient(IConfiguration configuration)
    {
        _factory = new ConnectionFactory()
        {
            HostName = configuration["RabbitMQHost"]!,
            Port = int.Parse(configuration["RabbitMQPort"]!)
        };
    }

    public async Task PublishNewOrderAsync(OrderPublishedEvent order)
    {
        var message = JsonSerializer.Serialize(order);
        var body = Encoding.UTF8.GetBytes(message);

        await _lock.WaitAsync();
        try
        {
            var channel = await GetChannelAsync();
            if (channel is null)
            {
                Log.Warning("--> RabbitMQ connection unavailable, not sending.");
                return;
            }

            await channel.BasicPublishAsync(exchange: Exchange, routingKey: RoutingKey, body: body);
            Log.Information("--> We have sent {Message}", message);
        }
        finally
        {
            _lock.Release();
        }
    }

    // Connects lazily so the service can start before RabbitMQ is up, and reconnects if the channel was closed.
    private async Task<IChannel?> GetChannelAsync()
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        try
        {
            await DisposeConnectionAsync();

            _connection = await _factory.CreateConnectionAsync();
            _connection.ConnectionShutdownAsync += RabbitMQ_ConnectionShutdownAsync;
            _channel = await _connection.CreateChannelAsync();
            await _channel.ExchangeDeclareAsync(exchange: Exchange, type: ExchangeType.Direct);
            Log.Information("--> Connected to MessageBus");
            return _channel;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not connect to a message bus: {Message}", ex.Message);
            await DisposeConnectionAsync();
            return null;
        }
    }

    private async Task DisposeConnectionAsync()
    {
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        if (_connection is not null)
        {
            _connection.ConnectionShutdownAsync -= RabbitMQ_ConnectionShutdownAsync;
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Log.Information("MessageBus Disposed");
        await _lock.WaitAsync();
        try
        {
            if (_channel is { IsOpen: true })
            {
                await _channel.CloseAsync();
            }

            if (_connection is { IsOpen: true })
            {
                await _connection.CloseAsync();
            }

            await DisposeConnectionAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task RabbitMQ_ConnectionShutdownAsync(object sender, ShutdownEventArgs e)
    {
        Log.Warning("--> RabbitMQ Connection Shutdown");
        return Task.CompletedTask;
    }
}
