using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CustomerService.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Serilog;

namespace CustomerService.AsyncDataServices;

public class MessageBusSubscriber : BackgroundService
{
    // Also declared by the chart's RabbitMQ definitions, with the same arguments.
    private const string Exchange = "order_events";
    private const string RoutingKey = "order.published";

    // Durable and named, so messages published while this service is down wait for it. With a
    // single active consumer only one pod receives at a time, so messages reach the inbox in
    // the order they were published.
    public const string Queue = "customer.order-events.v2";

    public static readonly Dictionary<string, object?> QueueArguments = new()
    {
        ["x-single-active-consumer"] = true,
    };
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly ConnectionFactory _factory;
    private readonly IInboxHandler _inbox;
    private readonly TimeSpan _retryDelay;
    private IConnection? _connection;
    private IChannel? _channel;

    public MessageBusSubscriber(
        IConfiguration configuration,
        IInboxHandler inbox,
        IOptions<MessagingOptions> options
    )
    {
        _inbox = inbox;
        _retryDelay = TimeSpan.FromSeconds(options.Value.RetryDelaySeconds);
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
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken
        );
        await _channel.QueueDeclareAsync(
            queue: Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: QueueArguments,
            cancellationToken: stoppingToken
        );
        await _channel.QueueBindAsync(
            queue: Queue,
            exchange: Exchange,
            routingKey: RoutingKey,
            cancellationToken: stoppingToken
        );

        // One unacknowledged message at a time keeps the inbox in publish order.
        await _channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 1,
            global: false,
            cancellationToken: stoppingToken
        );

        var channel = _channel;
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) =>
            HandleDeliveryAsync(channel, _inbox, ea, _retryDelay, stoppingToken);

        await _channel.BasicConsumeAsync(
            queue: Queue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken
        );

        Log.Information("--> Listening on the Message Bus....");
    }

    // Acknowledges a message once the inbox has processed it (or given up on it, or seen it
    // before). Otherwise it goes back to the queue after retryDelay, so it is never lost; with
    // prefetch 1 and a single active consumer, the messages after it wait meanwhile.
    public static async Task HandleDeliveryAsync(
        IChannel channel,
        IInboxHandler inbox,
        BasicDeliverEventArgs ea,
        TimeSpan retryDelay,
        CancellationToken cancellationToken
    )
    {
        var message = Encoding.UTF8.GetString(ea.Body.Span);

        DeliveryOutcome outcome;
        try
        {
            outcome = await inbox.HandleAsync(
                message,
                ea.BasicProperties.MessageId,
                cancellationToken
            );
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Log.Warning(ex, "--> Could not record the message in the inbox: {Message}", message);
            outcome = DeliveryOutcome.Retry;
        }

        if (outcome == DeliveryOutcome.Retry)
        {
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

        // The host may stop twice (e.g. a test host); the second close finds it already closed.
        try
        {
            if (_channel is { IsOpen: true })
            {
                await _channel.CloseAsync(cancellationToken);
            }

            if (_connection is { IsOpen: true })
            {
                await _connection.CloseAsync(cancellationToken);
            }
        }
        catch (AlreadyClosedException) { }

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
