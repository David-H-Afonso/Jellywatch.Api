using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Domain;

public class SyncJob
{
    public int Id { get; set; }
    public SyncJobType Type { get; set; }
    public SyncJobStatus Status { get; set; } = SyncJobStatus.Pending;
    public int? ProfileId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int ItemsProcessed { get; set; }
    public string? ErrorMessage { get; set; }

    public virtual Profile? Profile { get; set; }
}
