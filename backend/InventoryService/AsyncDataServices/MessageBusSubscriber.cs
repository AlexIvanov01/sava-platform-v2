using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InventoryService.EventProcessing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;

namespace InventoryService.AsyncDataServices;

public class MessageBusSubscriber : BackgroundService
{
    private const string Exchange = "order_exchange";
    private const string RoutingKey = "order.published";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly ConnectionFactory _factory;
    private readonly IEventProcessor _eventProcessor;
    private IConnection? _connection;
    private IChannel? _channel;

    public MessageBusSubscriber(IConfiguration configuration, IEventProcessor eventProcessor)
    {
        _eventProcessor = eventProcessor;
        _factory = new ConnectionFactory()
        {
            HostName = configuration["RabbitMQHost"]!,
            Port = int.Parse(configuration["RabbitMQPort"]!),
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Retry until the broker is reachable; after that the client's automatic recovery handles dropped connections.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await StartConsumingAsync(stoppingToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Error(
                    ex,
                    "--> Could not connect to Message Bus to listen for events: {Ex}",
                    ex.Message
                );
                await DisposeConnectionAsync();
            }

            try
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task StartConsumingAsync(CancellationToken stoppingToken)
    {
        _connection = await _factory.CreateConnectionAsync(stoppingToken);
        _connection.ConnectionShutdownAsync += RabbitMQ_ConnectionShutdownAsync;
        _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await _channel.ExchangeDeclareAsync(
            exchange: Exchange,
            type: ExchangeType.Direct,
            cancellationToken: stoppingToken
        );
        var queueName = (
            await _channel.QueueDeclareAsync(cancellationToken: stoppingToken)
        ).QueueName;
        await _channel.QueueBindAsync(
            queue: queueName,
            exchange: Exchange,
            routingKey: RoutingKey,
            cancellationToken: stoppingToken
        );

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            Log.Information("--> Event Received!");

            var notificationMessage = Encoding.UTF8.GetString(ea.Body.Span);

            try
            {
                await _eventProcessor.ProcessEvent(notificationMessage);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "--> Could not process event: {Ex}", ex.Message);
            }
        };

        await _channel.BasicConsumeAsync(
            queue: queueName,
            autoAck: true,
            consumer: consumer,
            cancellationToken: stoppingToken
        );

        Log.Information("--> Listening on the Message Bus....");
    }

    private static Task RabbitMQ_ConnectionShutdownAsync(object sender, ShutdownEventArgs e)
    {
        Log.Information("--> Connection Shutdown");
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);

        if (_channel is { IsOpen: true })
        {
            await _channel.CloseAsync(cancellationToken);
        }

        if (_connection is { IsOpen: true })
        {
            await _connection.CloseAsync(cancellationToken);
        }

        await DisposeConnectionAsync();
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
}
