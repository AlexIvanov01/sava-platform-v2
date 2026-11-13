namespace OrderService.Messaging;

// The "Messaging" configuration section (OutboxPublisher).
public class MessagingOptions
{
    public const string Section = "Messaging";

    // Messages are sent as soon as their order is saved; this sweep only picks up what that
    // missed (broker down, pod restarted) and retries that are due.
    public int SweepIntervalMinutes { get; set; } = 15;
    public int BatchSize { get; set; } = 50;
    public int MaxAttempts { get; set; } = 10;
    public int RetryBaseDelaySeconds { get; set; } = 30;
    public int RetryMaxDelaySeconds { get; set; } = 300;
    public int PublishTimeoutSeconds { get; set; } = 10;

    // Sent and superseded messages are deleted after this many days; Failed ones are kept.
    public int RetentionDays { get; set; } = 7;

    // The nightly cleanup runs at this hour (UTC; 01:00 is 03:00 in Sofia in winter).
    public int CleanupHourUtc { get; set; } = 1;
}
