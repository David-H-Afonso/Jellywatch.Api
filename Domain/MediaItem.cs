using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Domain;

public class MediaItem
{
    public int Id { get; set; }
    public MediaType MediaType { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? OriginalTitle { get; set; }
    public string? Overview { get; set; }
    public int? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public int? TvdbId { get; set; }
    public int? TvMazeId { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public string? ReleaseDate { get; set; }
    public string? Status { get; set; }
    public string? OriginalLanguage { get; set; }
    public string? Genres { get; set; }  // Comma-separated genre list from TMDB
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual Series? Series { get; set; }
    public virtual Movie? Movie { get; set; }
    public virtual ICollection<ExternalRating> ExternalRatings { get; set; } = new List<ExternalRating>();
    public virtual ICollection<MediaImage> Images { get; set; } = new List<MediaImage>();
    public virtual ICollection<MediaTranslation> Translations { get; set; } = new List<MediaTranslation>();
    public virtual ICollection<ProfileWatchState> WatchStates { get; set; } = new List<ProfileWatchState>();
    public virtual ICollection<ProfileNote> Notes { get; set; } = new List<ProfileNote>();
}
