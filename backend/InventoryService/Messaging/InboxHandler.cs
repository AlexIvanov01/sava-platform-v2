using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using InventoryService.DataAccess;
using InventoryService.Dtos;
using InventoryService.EventProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Serilog;

namespace InventoryService.Messaging;

public enum DeliveryOutcome
{
    // Done with: processed, a duplicate, or given up on (Failed).
    Ack,

    // Failed for now; put it back on the queue for another attempt.
    Retry,
}

public interface IInboxHandler
{
    // Records the message in the inbox and processes it. Throws only if the outcome couldn't be
    // recorded (e.g. the database is down); the message must then go back to the queue.
    Task<DeliveryOutcome> HandleAsync(string body, string? amqpMessageId, CancellationToken ct);
}

// Called by MessageBusSubscriber for every delivery. The event's changes and the inbox row are
// committed in one transaction, so a message is processed exactly once even when redelivered.
// Messages that can't be processed (not JSON, no MessageId, unknown event, no OrderId, or one the
// processor rejects) are stored as Failed.
public class InboxHandler(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<MessagingOptions> options
) : IInboxHandler
{
    public const string OrderPublished = "Order_Published";
    public const string OrderDeleted = "Order_Deleted";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<DeliveryOutcome> HandleAsync(
        string body,
        string? amqpMessageId,
        CancellationToken ct
    )
    {
        var received = Parse(body, amqpMessageId, Now);

        Exception failure;
        using (var scope = scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ProductContext>();
            var existing = await db.InboxMessages.SingleOrDefaultAsync(
                m => m.Id == received.Id,
                ct
            );
            if (existing is { Status: not InboxStatus.Received })
            {
                Log.Information(
                    "--> Message {MessageId} was already handled ({Status}).",
                    existing.Id,
                    existing.Status
                );
                return DeliveryOutcome.Ack;
            }
            if (received.Status == InboxStatus.Failed)
            {
                db.InboxMessages.Add(received);
                await db.SaveChangesAsync(ct);
                Log.Error(
                    "--> Stored unusable message {MessageId} as Failed: {Reason}",
                    received.Id,
                    received.LastError
                );
                return DeliveryOutcome.Ack;
            }

            var row = existing ?? received;
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await scope
                    .ServiceProvider.GetRequiredService<IEventProcessor>()
                    .ProcessEvent(body);
                if (existing == null)
                {
                    db.InboxMessages.Add(row);
                }
                row.Attempts++;
                row.Status = InboxStatus.Completed;
                row.ProcessedAt = Now;
                row.LastError = null;
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                Log.Information(
                    "--> Processed {Type} {MessageId} for order {OrderId}.",
                    row.Type,
                    row.Id,
                    row.AggregateId
                );
                return DeliveryOutcome.Ack;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                failure = ex;
            }
        }

        return await RecordFailureAsync(received, failure, ct);
    }

    // In a fresh context: the failed one may hold half-applied changes.
    private async Task<DeliveryOutcome> RecordFailureAsync(
        InboxMessage received,
        Exception ex,
        CancellationToken ct
    )
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductContext>();
        var row = await db.InboxMessages.SingleOrDefaultAsync(m => m.Id == received.Id, ct);
        if (row == null)
        {
            row = received;
            db.InboxMessages.Add(row);
        }
        row.Attempts++;
        row.LastError = Retry.Truncate(ex.Message);

        var giveUp = ex is InvalidEventException || row.Attempts >= options.Value.MaxAttempts;
        if (giveUp)
        {
            row.Status = InboxStatus.Failed;
            row.ProcessedAt = Now;
        }
        await db.SaveChangesAsync(ct);

        if (giveUp)
        {
            Log.Error(
                ex,
                "--> Giving up on {Type} {MessageId} for order {OrderId} after {Attempts} attempts: {Reason}",
                row.Type,
                row.Id,
                row.AggregateId,
                row.Attempts,
                ex.Message
            );
            return DeliveryOutcome.Ack;
        }
        Log.Warning(
            ex,
            "--> Could not process {Type} {MessageId} (attempt {Attempts}); retrying.",
            row.Type,
            row.Id,
            row.Attempts
        );
        return DeliveryOutcome.Retry;
    }

    public static InboxMessage Parse(string body, string? amqpMessageId, DateTime now)
    {
        GenericEventDto? envelope = null;
        string? problem = null;
        try
        {
            envelope = JsonSerializer.Deserialize<GenericEventDto>(body);
        }
        catch (JsonException ex)
        {
            problem = $"The message is not a valid event: {ex.Message}";
        }

        Guid? messageId =
            envelope?.MessageId is { } id && id != Guid.Empty ? id
            : Guid.TryParse(amqpMessageId, out var amqpId) && amqpId != Guid.Empty ? amqpId
            : null;

        problem ??= envelope switch
        {
            null => "The message is empty.",
            _ when messageId == null => "The message has no MessageId.",
            { Event: not (OrderPublished or OrderDeleted) } => $"Unknown event '{envelope.Event}'.",
            { OrderId: null } => "The message has no OrderId.",
            _ when envelope.OrderId == Guid.Empty => "The message has no OrderId.",
            _ => null,
        };

        var eventName = envelope?.Event;
        return new InboxMessage
        {
            Id = messageId ?? Guid.NewGuid(),
            Type = eventName is { Length: > 50 } ? eventName[..50] : eventName,
            AggregateId = problem == null ? envelope!.OrderId : null,
            Payload = body,
            Status = problem == null ? InboxStatus.Received : InboxStatus.Failed,
            LastError = problem == null ? null : Retry.Truncate(problem),
            CreatedAt = now,
            ProcessedAt = problem == null ? null : now,
        };
    }
}

public static class Retry
{
    public static string Truncate(string message) =>
        message.Length <= 2000 ? message : message[..2000];
}
