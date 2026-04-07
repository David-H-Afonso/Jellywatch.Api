namespace Jellywatch.Api.Domain;

public class Movie
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public int? Runtime { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual MediaItem MediaItem { get; set; } = null!;
    public virtual ICollection<ProfileWatchState> WatchStates { get; set; } = new List<ProfileWatchState>();
    public virtual ICollection<WatchEvent> WatchEvents { get; set; } = new List<WatchEvent>();
}
