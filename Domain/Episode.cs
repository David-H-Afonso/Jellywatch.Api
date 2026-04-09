namespace Jellywatch.Api.Domain;

public class Episode
{
    public int Id { get; set; }
    public int SeasonId { get; set; }
    public int EpisodeNumber { get; set; }
    public string? Name { get; set; }
    public string? Overview { get; set; }
    public string? StillPath { get; set; }
    public int? TmdbId { get; set; }
    public string? AirDate { get; set; }
    public string? AirTime { get; set; }
    public string? AirTimeUtc { get; set; }
    public int? Runtime { get; set; }
    public double? TmdbRating { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual Season Season { get; set; } = null!;
    public virtual ICollection<ProfileWatchState> WatchStates { get; set; } = new List<ProfileWatchState>();
    public virtual ICollection<WatchEvent> WatchEvents { get; set; } = new List<WatchEvent>();
    public virtual ICollection<ProfileNote> Notes { get; set; } = new List<ProfileNote>();
}
