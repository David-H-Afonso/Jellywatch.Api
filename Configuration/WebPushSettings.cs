namespace Jellywatch.Api.Configuration;

public sealed class WebPushSettings
{
    public const string SectionName = "WebPush";

    public bool Enabled { get; set; } = true;
    public string PublicKey { get; set; } = string.Empty;
    public string PrivateKey { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public int WorkerIntervalSeconds { get; set; } = 300;
    public bool MetadataRefreshEnabled { get; set; } = true;
    public int MetadataDailyHour { get; set; } = 3;
    public string MetadataTimeZoneId { get; set; } = "Europe/Madrid";
    public int MetadataItemDelaySeconds { get; set; } = 5;
    public int MaxAttempts { get; set; } = 3;

    public bool HasVapidConfiguration =>
        !string.IsNullOrWhiteSpace(PublicKey)
        && !string.IsNullOrWhiteSpace(PrivateKey)
        && !string.IsNullOrWhiteSpace(Subject);
}
