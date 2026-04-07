using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Domain;

public class ImportQueueItem
{
    public int Id { get; set; }
    public string JellyfinItemId { get; set; } = string.Empty;
    public MediaType MediaType { get; set; }
    public int Priority { get; set; }
    public ImportStatus Status { get; set; } = ImportStatus.Pending;
    public int RetryCount { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
