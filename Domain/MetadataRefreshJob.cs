using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Domain;

public class MetadataRefreshJob
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public ExternalProvider Provider { get; set; }
    public ImportStatus Status { get; set; } = ImportStatus.Pending;
    public DateTime? LastRefreshed { get; set; }
    public DateTime? NextRefresh { get; set; }

    public virtual MediaItem MediaItem { get; set; } = null!;
}
