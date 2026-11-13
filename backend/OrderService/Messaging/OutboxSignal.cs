using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace OrderService.Messaging;

// Wakes OutboxPublisher when OrderContext has saved new outbox messages.
public sealed class OutboxSignal
{
    private readonly Channel<bool> _channel = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite }
    );

    public void Notify() => _channel.Writer.TryWrite(true);

    // Returns when notified or after the timeout.
    public async Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await _channel.Reader.ReadAsync(cts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }
}
