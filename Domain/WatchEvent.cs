using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Domain;

public class WatchEvent
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int MediaItemId { get; set; }
    public int? EpisodeId { get; set; }
    public int? MovieId { get; set; }
    public string? JellyfinItemId { get; set; }
    public WatchEventType EventType { get; set; }
    public long? PositionTicks { get; set; }
    public SyncSource Source { get; set; }
    public DateTime Timestamp { get; set; }

    public virtual Profile Profile { get; set; } = null!;
    public virtual MediaItem MediaItem { get; set; } = null!;
    public virtual Episode? Episode { get; set; }
    public virtual Movie? Movie { get; set; }
}
