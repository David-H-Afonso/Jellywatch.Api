namespace Jellywatch.Api.Domain;

public class Season
{
    public int Id { get; set; }
    public int SeriesId { get; set; }
    public int SeasonNumber { get; set; }
    public string? Name { get; set; }
    public string? Overview { get; set; }
    public string? PosterPath { get; set; }
    public int? TmdbId { get; set; }
    public int? EpisodeCount { get; set; }
    public string? AirDate { get; set; }
    public double? TmdbRating { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual Series Series { get; set; } = null!;
    public virtual ICollection<Episode> Episodes { get; set; } = new List<Episode>();
    public virtual ICollection<ProfileNote> Notes { get; set; } = new List<ProfileNote>();
}
