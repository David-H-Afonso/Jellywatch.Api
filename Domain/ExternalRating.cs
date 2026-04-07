using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Domain;

public class ExternalRating
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public ExternalProvider Provider { get; set; }
    public string? Score { get; set; }
    public int? VoteCount { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual MediaItem MediaItem { get; set; } = null!;
}
