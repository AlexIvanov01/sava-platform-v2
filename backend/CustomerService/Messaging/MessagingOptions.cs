namespace CustomerService.Messaging;

// The "Messaging" configuration section (MessageBusSubscriber, InboxHandler, InboxCleanup).
public class MessagingOptions
{
    public const string Section = "Messaging";

    // A message that fails goes back to the queue after RetryDelaySeconds; after MaxAttempts it
    // is marked Failed and acknowledged, so it can't block the queue.
    public int MaxAttempts { get; set; } = 5;
    public int RetryDelaySeconds { get; set; } = 5;

    // Completed messages are deleted after this many days; Failed ones are kept.
    public int RetentionDays { get; set; } = 7;

    // The nightly cleanup runs at this hour (UTC; 01:00 is 03:00 in Sofia in winter).
    public int CleanupHourUtc { get; set; } = 1;
}
