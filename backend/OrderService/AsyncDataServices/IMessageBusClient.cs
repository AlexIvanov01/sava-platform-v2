using System;
using System.Threading;
using System.Threading.Tasks;

namespace OrderService.AsyncDataServices;

public interface IMessageBusClient
{
    // Returns once the broker has confirmed the message. Throws MessageBusUnavailableException
    // when no connection can be made; any other exception means this message wasn't accepted.
    Task PublishAsync(Guid messageId, string eventType, string payload, CancellationToken ct);
}

public class MessageBusUnavailableException(Exception inner)
    : Exception("The message bus is unavailable.", inner);
