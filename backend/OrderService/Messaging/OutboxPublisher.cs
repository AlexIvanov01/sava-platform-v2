using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OrderService.AsyncDataServices;
using OrderService.DataAccess;
using Serilog;

namespace OrderService.Messaging;

// Sends outbox messages to RabbitMQ. It wakes as soon as OrderContext saves new messages and
// otherwise every SweepIntervalMinutes, to retry what failed or was missed. A MySQL named lock
// lets only one pod send at a time, which keeps the messages in order.
public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IMessageBusClient bus,
    OutboxSignal signal,
    TimeProvider clock,
    IOptions<MessagingOptions> options
) : BackgroundService
{
    private readonly MessagingOptions _options = options.Value;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "--> Could not process the outbox: {Message}", ex.Message);
            }

            try
            {
                await signal.WaitAsync(
                    TimeSpan.FromMinutes(_options.SweepIntervalMinutes),
                    stoppingToken
                );
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // Sends every due message, oldest first. Only the newest message of an order is sent; older
    // unsent ones are Superseded, since each carries the order's full state. A failed send is
    // retried with backoff and given up (Failed) after MaxAttempts; when the broker can't be
    // reached, the run stops without using up any attempts. Returns false if another pod is
    // sending.
    public async Task<bool> PublishDueAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderContext>();

        // GET_LOCK belongs to the connection, so keep one open for the whole run.
        await db.Database.OpenConnectionAsync(ct);
        if (!await TryLockAsync(db, ct))
        {
            return false;
        }
        try
        {
            while (await PublishBatchAsync(db, ct)) { }
        }
        catch (MessageBusUnavailableException)
        {
            Log.Warning(
                "--> The message bus is unavailable; outbox messages wait for the next sweep."
            );
        }
        finally
        {
            await db
                .Database.SqlQueryRaw<int?>(
                    "SELECT RELEASE_LOCK(CONCAT('outbox:', DATABASE())) AS Value"
                )
                .SingleAsync(CancellationToken.None);
        }
        return true;
    }

    // False when there was nothing due.
    private async Task<bool> PublishBatchAsync(OrderContext db, CancellationToken ct)
    {
        var now = Now;
        var due = await db
            .OutboxMessages.Where(m =>
                m.Status == OutboxStatus.Pending
                && (m.NextAttemptAt == null || m.NextAttemptAt <= now)
            )
            .OrderBy(m => m.Sequence)
            .Take(_options.BatchSize)
            .ToListAsync(ct);
        if (due.Count == 0)
        {
            return false;
        }
        var latest = await LatestSequencesAsync(db, due, ct);

        foreach (var message in due)
        {
            if (message.Sequence < latest[message.AggregateId])
            {
                message.Status = OutboxStatus.Superseded;
                message.ProcessedAt = Now;
                message.NextAttemptAt = null;
                await db.SaveChangesAsync(ct);
                continue;
            }

            try
            {
                await bus.PublishAsync(message.Id, message.Type!, message.Payload!, ct);
                message.Attempts++;
                message.Status = OutboxStatus.Sent;
                message.ProcessedAt = Now;
                message.NextAttemptAt = null;
                message.LastError = null;
            }
            catch (Exception ex)
                when (ex is not MessageBusUnavailableException && !ct.IsCancellationRequested)
            {
                RecordFailure(message, ex);
            }
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    private void RecordFailure(OutboxMessage message, Exception ex)
    {
        message.Attempts++;
        message.LastError = Retry.Truncate(ex.Message);
        if (message.Attempts >= _options.MaxAttempts)
        {
            message.Status = OutboxStatus.Failed;
            message.NextAttemptAt = null;
            message.ProcessedAt = Now;
            Log.Error(
                ex,
                "--> Giving up on sending {Type} {MessageId} for order {OrderId} after {Attempts} attempts.",
                message.Type,
                message.Id,
                message.AggregateId,
                message.Attempts
            );
            return;
        }

        message.NextAttemptAt = Retry.NextAttemptAt(message.Attempts, Now, _options);
        Log.Warning(
            ex,
            "--> Could not send {Type} {MessageId}; retrying at {NextAttemptAt}.",
            message.Type,
            message.Id,
            message.NextAttemptAt
        );
    }

    private static async Task<bool> TryLockAsync(OrderContext db, CancellationToken ct) =>
        await db
            .Database.SqlQueryRaw<int?>(
                "SELECT GET_LOCK(CONCAT('outbox:', DATABASE()), 0) AS Value"
            )
            .SingleAsync(ct) == 1;

    private static async Task<Dictionary<Guid, long>> LatestSequencesAsync(
        OrderContext db,
        List<OutboxMessage> due,
        CancellationToken ct
    )
    {
        var orderIds = due.Select(m => m.AggregateId).Distinct().ToList();
        return await db
            .OutboxMessages.Where(m => orderIds.Contains(m.AggregateId))
            .GroupBy(m => m.AggregateId)
            .Select(g => new { OrderId = g.Key, Latest = g.Max(m => m.Sequence) })
            .ToDictionaryAsync(x => x.OrderId, x => x.Latest, ct);
    }
}
