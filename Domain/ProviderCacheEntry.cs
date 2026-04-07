using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Domain;

public class ProviderCacheEntry
{
    public int Id { get; set; }
    public ExternalProvider Provider { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string? ResponseJson { get; set; }
    public DateTime CachedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
