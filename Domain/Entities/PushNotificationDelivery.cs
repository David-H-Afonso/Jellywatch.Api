namespace Jellywatch.Api.Domain.Entities;

public sealed class PushNotificationDelivery
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int PushSubscriptionId { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string Url { get; set; } = "/series";
    public DateTime DueAtUtc { get; set; }
    public DateTime? SentAtUtc { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public string Status { get; set; } = "Pending";
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; }

    public User User { get; set; } = null!;
    public PushSubscription PushSubscription { get; set; } = null!;
}
