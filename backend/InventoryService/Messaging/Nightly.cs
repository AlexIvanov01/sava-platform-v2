using System;
using System.Threading;
using System.Threading.Tasks;

namespace InventoryService.Messaging;

public static class Nightly
{
    // The next time it is hourUtc:00 UTC, after now.
    public static DateTime NextRun(DateTime now, int hourUtc)
    {
        var today = now.Date.AddHours(hourUtc);
        return today > now ? today : today.AddDays(1);
    }

    public static Task DelayUntilNextRunAsync(TimeProvider clock, int hourUtc, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        return Task.Delay(NextRun(now, hourUtc) - now, clock, ct);
    }
}
