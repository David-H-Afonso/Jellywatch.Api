using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Domain;

public class ProfileWatchState
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int MediaItemId { get; set; }
    public int? EpisodeId { get; set; }
    public int? SeasonId { get; set; }
    public int? MovieId { get; set; }
    public WatchState State { get; set; } = WatchState.Unseen;
    public bool IsManualOverride { get; set; }
    public decimal? UserRating { get; set; }
    public DateTime LastUpdated { get; set; }

    public virtual Profile Profile { get; set; } = null!;
    public virtual MediaItem MediaItem { get; set; } = null!;
    public virtual Episode? Episode { get; set; }
    public virtual Season? Season { get; set; }
    public virtual Movie? Movie { get; set; }
}
