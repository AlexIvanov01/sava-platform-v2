using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CustomerService.EventProcessing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Serilog;

namespace CustomerService.AsyncDataServices;

public class MessageBusSubscriber : BackgroundService
{
    private const string Exchange = "order_exchange";
    private const string RoutingKey = "order.published";

    // Durable and named, so messages published while this service is down wait for it.
    public const string Queue = "customer.order-events";
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
            UserName = configuration["RabbitMQUser"] ?? ConnectionFactory.DefaultUser,
            Password = configuration["RabbitMQPassword"] ?? ConnectionFactory.DefaultPass,
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
        await _channel.QueueDeclareAsync(
            queue: Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken
        );
        await _channel.QueueBindAsync(
            queue: Queue,
            exchange: Exchange,
            routingKey: RoutingKey,
            cancellationToken: stoppingToken
        );

        // One unacknowledged message at a time keeps events in order.
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false,
            cancellationToken: stoppingToken
        );

        var channel = _channel;
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) =>
            HandleDeliveryAsync(channel, _eventProcessor, ea, RetryDelay, stoppingToken);

        await _channel.BasicConsumeAsync(
            queue: Queue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken
        );

        Log.Information("--> Listening on the Message Bus....");
    }

    // Acknowledges a message only after it was processed. A failure is retried once (after
    // retryDelay, to ride out e.g. a database restart); if the redelivery fails too, the message is
    // dropped and logged so it can't block the queue. Messages the processor rejects as invalid
    // don't throw, so they are acknowledged and not retried.
    public static async Task HandleDeliveryAsync(
        IChannel channel,
        IEventProcessor eventProcessor,
        BasicDeliverEventArgs ea,
        TimeSpan retryDelay,
        CancellationToken cancellationToken
    )
    {
        Log.Information("--> Event Received!");
        var message = Encoding.UTF8.GetString(ea.Body.Span);

        try
        {
            await eventProcessor.ProcessEvent(message);
        }
        catch (Exception ex)
        {
            if (ea.Redelivered)
            {
                Log.Error(ex, "--> Could not process event again; dropping it: {Message}", message);
                await channel.BasicNackAsync(ea.DeliveryTag, false, false, cancellationToken);
                return;
            }

            Log.Warning(ex, "--> Could not process event; retrying it once: {Message}", message);
            await Task.Delay(retryDelay, cancellationToken);
            await channel.BasicNackAsync(ea.DeliveryTag, false, true, cancellationToken);
            return;
        }

        await channel.BasicAckAsync(ea.DeliveryTag, false, cancellationToken);
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
