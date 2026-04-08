using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Contracts;

public class SeriesListDto
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? PosterPath { get; set; }
    public string? Status { get; set; }
    public int? TotalSeasons { get; set; }
    public int? TotalEpisodes { get; set; }
    public int EpisodesSeen { get; set; }
    public WatchState AggregateState { get; set; }
    public string? ReleaseDate { get; set; }
    public decimal? UserRating { get; set; }
}

public class SeriesDetailDto
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? OriginalTitle { get; set; }
    public string? Overview { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public string? ReleaseDate { get; set; }
    public string? Status { get; set; }
    public string? OriginalLanguage { get; set; }
    public string? Network { get; set; }
    public int? TotalSeasons { get; set; }
    public int? TotalEpisodes { get; set; }
    public List<ExternalRatingDto> Ratings { get; set; } = new();
    public List<SeasonDto> Seasons { get; set; } = new();
    public decimal? UserRating { get; set; }
    public TranslationDto? SpanishTranslation { get; set; }
    public bool IsBlocked { get; set; }
}

public class MovieListDto
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? PosterPath { get; set; }
    public int? Runtime { get; set; }
    public WatchState State { get; set; }
    public string? ReleaseDate { get; set; }
    public decimal? UserRating { get; set; }
}

public class MovieDetailDto
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? OriginalTitle { get; set; }
    public string? Overview { get; set; }
    public string? PosterPath { get; set; }
    public string? BackdropPath { get; set; }
    public string? ReleaseDate { get; set; }
    public string? OriginalLanguage { get; set; }
    public int? Runtime { get; set; }
    public WatchState State { get; set; }
    public decimal? UserRating { get; set; }
    public List<ExternalRatingDto> Ratings { get; set; } = new();
    public TranslationDto? SpanishTranslation { get; set; }
    public bool IsBlocked { get; set; }
    public DateTime? WatchedAt { get; set; }
}

public class SeasonDto
{
    public int Id { get; set; }
    public int SeasonNumber { get; set; }
    public string? Name { get; set; }
    public string? Overview { get; set; }
    public string? PosterPath { get; set; }
    public string? PosterUrl { get; set; }
    public int? EpisodeCount { get; set; }
    public string? AirDate { get; set; }
    public double? TmdbRating { get; set; }
    public int EpisodesSeen { get; set; }
    public decimal? UserRating { get; set; }
    public List<EpisodeDto> Episodes { get; set; } = new();
}

public class EpisodeDto
{
    public int Id { get; set; }
    public int EpisodeNumber { get; set; }
    public string? Name { get; set; }
    public string? Overview { get; set; }
    public string? StillPath { get; set; }
    public string? StillUrl { get; set; }
    public string? AirDate { get; set; }
    public int? Runtime { get; set; }
    public double? TmdbRating { get; set; }
    public WatchState State { get; set; }
    public bool IsManualOverride { get; set; }
    public decimal? UserRating { get; set; }
    public DateTime? WatchedAt { get; set; }
}

public class ExternalRatingDto
{
    public ExternalProvider Provider { get; set; }
    public string? Score { get; set; }
    public int? VoteCount { get; set; }
}

public class TranslationDto
{
    public string Language { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Overview { get; set; }
}

public class WatchStateUpdateDto
{
    public WatchState State { get; set; }
    public DateTime? Timestamp { get; set; }
}

public class UserRatingDto
{
    public decimal? Rating { get; set; }
}

public class ActivityDto
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public int? SeriesId { get; set; }
    public int? MovieId { get; set; }
    public string MediaTitle { get; set; } = string.Empty;
    public string? EpisodeName { get; set; }
    public int? EpisodeNumber { get; set; }
    public int? SeasonNumber { get; set; }
    public MediaType MediaType { get; set; }
    public WatchEventType EventType { get; set; }
    public SyncSource Source { get; set; }
    public DateTime Timestamp { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? PosterPath { get; set; }
    public decimal? UserRating { get; set; }
    public double? TmdbRating { get; set; }
}

public class ProfileBlockedItemDto
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SpanishTitle { get; set; }
    public MediaType MediaType { get; set; }
    public DateTime BlockedAt { get; set; }
}

public class AdminProfileBlockDto
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public string ProfileName { get; set; } = string.Empty;
    public int MediaItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? SpanishTitle { get; set; }
    public MediaType MediaType { get; set; }
    public DateTime BlockedAt { get; set; }
}
