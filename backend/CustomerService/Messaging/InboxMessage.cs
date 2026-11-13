using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace CustomerService.Messaging;

// A message received from the bus, recorded by InboxHandler together with its processing.
public class InboxMessage
{
    // The publisher's MessageId, so a redelivered message isn't processed twice. A message
    // without one gets a generated Id and is stored as Failed.
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }

    // Arrival order.
    public long Sequence { get; set; }

    [MaxLength(50)]
    public string? Type { get; set; }

    // The order the event is about. Null for messages that were unusable on arrival.
    public Guid? AggregateId { get; set; }

    // The message body as received.
    public string? Payload { get; set; }

    [MaxLength(20)]
    public InboxStatus Status { get; set; } = InboxStatus.Received;

    public int Attempts { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    // When it first arrived.
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<InboxStatus>))]
public enum InboxStatus
{
    // Failed at least once and waiting on the queue for another attempt.
    Received,
    Completed,
    Failed,
}

// Thrown by the event processor for a message that can never be processed; it is marked Failed
// without further attempts.
public class InvalidEventException(string message) : Exception(message);
