using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using InventoryService.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;

namespace InventoryService.Messaging;

// Deletes completed inbox messages older than RetentionDays, once a night at CleanupHourUtc.
// Failed messages stay until someone replays or deletes them.
public sealed class InboxCleanup(
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
                Log.Warning(ex, "--> Could not clean up the inbox: {Message}", ex.Message);
            }
        }
    }

    public async Task<int> CleanupAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ProductContext>();
        var cutoff = clock.GetUtcNow().UtcDateTime.AddDays(-options.Value.RetentionDays);
        var deleted = await db
            .InboxMessages.Where(m => m.Status == InboxStatus.Completed && m.CreatedAt < cutoff)
            .ExecuteDeleteAsync(ct);
        if (deleted > 0)
        {
            Log.Information("--> Deleted {Count} old inbox messages.", deleted);
        }
        return deleted;
    }
}
