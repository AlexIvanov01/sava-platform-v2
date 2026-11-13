using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace OrderService.Messaging;

// An event waiting to be sent, written in the same transaction as the change it describes
// (OrderContext turns an order's raised events into these). OutboxPublisher sends them.
public class OutboxMessage
{
    // Also the message's MessageId, both in the AMQP properties and in the JSON body.
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }

    // Insertion order; events are sent in this order.
    public long Sequence { get; set; }

    [MaxLength(50)]
    public string? Type { get; set; }

    // The order the event is about. A newer event of the same order supersedes an unsent one.
    public Guid AggregateId { get; set; }

    public string? Payload { get; set; }

    [MaxLength(20)]
    public OutboxStatus Status { get; set; } = OutboxStatus.Pending;

    public int Attempts { get; set; }

    [MaxLength(2000)]
    public string? LastError { get; set; }

    public DateTime? NextAttemptAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<OutboxStatus>))]
public enum OutboxStatus
{
    Pending,
    Sent,
    Failed,
    Superseded,
}
