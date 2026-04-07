namespace Jellywatch.Api.Domain;

public class ProfileNote
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int MediaItemId { get; set; }
    public int? SeasonId { get; set; }
    public int? EpisodeId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual Profile Profile { get; set; } = null!;
    public virtual MediaItem MediaItem { get; set; } = null!;
    public virtual Season? Season { get; set; }
    public virtual Episode? Episode { get; set; }
}
