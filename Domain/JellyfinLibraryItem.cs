using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Domain;

public class JellyfinLibraryItem
{
    public int Id { get; set; }
    public string JellyfinItemId { get; set; } = string.Empty;
    public string? JellyfinParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public MediaType? Type { get; set; }
    public int? MediaItemId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual MediaItem? MediaItem { get; set; }
}
