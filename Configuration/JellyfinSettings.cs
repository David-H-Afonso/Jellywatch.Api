namespace Jellywatch.Api.Configuration;

public class JellyfinSettings
{
    public const string SectionName = "JellyfinSettings";

    public string BaseUrl { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string? WebhookSecret { get; set; }
    public int PollingIntervalMinutes { get; set; } = 60;
    public bool PollingEnabled { get; set; } = true;
}
