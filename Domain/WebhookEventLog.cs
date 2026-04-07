namespace Jellywatch.Api.Domain;

public class WebhookEventLog
{
    public int Id { get; set; }
    public string? RawPayload { get; set; }
    public string? EventType { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
