using System;

namespace OrderService.Messaging;

public static class Retry
{
    // Exponential backoff: RetryBaseDelaySeconds after the first failure, doubling each time, at
    // most RetryMaxDelaySeconds.
    public static DateTime NextAttemptAt(int attempts, DateTime now, MessagingOptions options)
    {
        var seconds = Math.Min(
            options.RetryMaxDelaySeconds,
            options.RetryBaseDelaySeconds * Math.Pow(2, Math.Max(0, attempts - 1))
        );
        return now.AddSeconds(seconds);
    }

    public static string Truncate(string message) =>
        message.Length <= 2000 ? message : message[..2000];
}
