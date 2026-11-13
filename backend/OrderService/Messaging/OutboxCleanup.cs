using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OrderService.DataAccess;
using Serilog;

namespace OrderService.Messaging;

// Deletes sent and superseded outbox messages older than RetentionDays, once a night at
// CleanupHourUtc. Failed messages stay until someone replays or deletes them.
public sealed class OutboxCleanup(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    IOptions<MessagingOptions> options
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Nightly.DelayUntilNextRunAsync(
                    clock,
                    options.Value.CleanupHourUtc,
                    stoppingToken
                );
                await CleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Tried again the next night.
                Log.Warning(ex, "--> Could not clean up the outbox: {Message}", ex.Message);
            }
        }
    }

    public async Task<int> CleanupAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderContext>();
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-options.Value.RetentionDays);
        var deleted = await db
            .OutboxMessages.Where(m =>
                (m.Status == OutboxStatus.Sent || m.Status == OutboxStatus.Superseded)
                && m.CreatedAt < cutoff
            )
            .ExecuteDeleteAsync(ct);
        if (deleted > 0)
        {
            Log.Information("--> Deleted {Count} old outbox messages.", deleted);
        }
        return deleted;
    }
}
