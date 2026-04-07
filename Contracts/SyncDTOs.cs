using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Contracts;

public class SyncJobDto
{
    public int Id { get; set; }
    public SyncJobType Type { get; set; }
    public SyncJobStatus Status { get; set; }
    public int? ProfileId { get; set; }
    public string? ProfileName { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int ItemsProcessed { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ProviderSettingsDto
{
    public bool TmdbEnabled { get; set; }
    public bool TmdbHasApiKey { get; set; }
    public bool OmdbEnabled { get; set; }
    public bool OmdbHasApiKey { get; set; }
    public bool TvMazeEnabled { get; set; }
    public string PrimaryLanguage { get; set; } = "en-US";
    public string? FallbackLanguage { get; set; }
}

public class WebhookEventLogDto
{
    public int Id { get; set; }
    public string? EventType { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
