using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using OrderService.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;

namespace OrderService.AsyncDataServices;

// Used only by OutboxPublisher. Publishes persistent messages with publisher confirms, so a
// message counts as sent only once the broker has stored it.
public sealed class MessageBusClient : IMessageBusClient, IAsyncDisposable
{
    // Also declared by the chart's RabbitMQ definitions and by the subscribers.
    public const string Exchange = "order_events";
    public const string RoutingKey = "order.published";

    private readonly ConnectionFactory _factory;
    private readonly TimeSpan _publishTimeout;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public MessageBusClient(IConfiguration configuration, IOptions<MessagingOptions> options)
    {
        _publishTimeout = TimeSpan.FromSeconds(options.Value.PublishTimeoutSeconds);
        _factory = new ConnectionFactory()
        {
            HostName = configuration["RabbitMQHost"]!,
            Port = int.Parse(configuration["RabbitMQPort"]!),
            UserName = configuration["RabbitMQUser"] ?? ConnectionFactory.DefaultUser,
            Password = configuration["RabbitMQPassword"] ?? ConnectionFactory.DefaultPass,
        };
    }

    public async Task PublishAsync(
        Guid messageId,
        string eventType,
        string payload,
        CancellationToken ct
    )
    {
        var body = Encoding.UTF8.GetBytes(payload);
        var properties = new BasicProperties
        {
            MessageId = messageId.ToString(),
            Type = eventType,
            ContentType = "application/json",
            Persistent = true,
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
        };

        await _lock.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_publishTimeout);
            // With confirmation tracking this waits for the broker's ack and throws on a nack or,
            // because of mandatory, when no queue is bound to take the message.
            await channel.BasicPublishAsync(
                exchange: Exchange,
                routingKey: RoutingKey,
                mandatory: true,
                basicProperties: properties,
                body: body,
                cancellationToken: timeout.Token
            );
            Log.Information("--> Sent {Type} {MessageId}", eventType, messageId);
        }
        finally
        {
            _lock.Release();
        }
    }

    // Connects lazily so the service can start before RabbitMQ is up, and reconnects if the channel was closed.
    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true })
        {
            return _channel;
        }

        try
        {
            await DisposeConnectionAsync();

            _connection = await _factory.CreateConnectionAsync(ct);
            _connection.ConnectionShutdownAsync += RabbitMQ_ConnectionShutdownAsync;
            _channel = await _connection.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: true,
                    publisherConfirmationTrackingEnabled: true
                ),
                ct
            );
            await _channel.ExchangeDeclareAsync(
                exchange: Exchange,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                cancellationToken: ct
            );
            Log.Information("--> Connected to MessageBus");
            return _channel;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Log.Warning(ex, "--> Could not connect to the message bus: {Message}", ex.Message);
            await DisposeConnectionAsync();
            throw new MessageBusUnavailableException(ex);
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

    private static Task RabbitMQ_ConnectionShutdownAsync(object sender, ShutdownEventArgs e)
    {
        Log.Warning("--> RabbitMQ Connection Shutdown");
        return Task.CompletedTask;
    }
}
