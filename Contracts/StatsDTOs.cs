namespace Jellywatch.Api.Contracts;

public class WrappedDto
{
    public int Year { get; set; }
    public int TotalEpisodesWatched { get; set; }
    public int TotalMoviesWatched { get; set; }
    public int TotalSeriesWatched { get; set; }
    public int TotalMinutesWatched { get; set; }
    public int TotalDaysActive { get; set; }
    public int LongestStreakDays { get; set; }
    public string? BusiestDay { get; set; }
    public int BusiestDayCount { get; set; }
    public List<MonthlyActivityDto> MonthlyActivity { get; set; } = new();
    public List<TopSeriesDto> TopSeries { get; set; } = new();
    public List<TopMovieDto> TopMovies { get; set; } = new();
    public WrappedMediaDto? FirstWatch { get; set; }
    public string? MostActiveMonth { get; set; }
    public int MostActiveMonthCount { get; set; }
    public List<TopNetworkDto> TopNetworks { get; set; } = new();
}

public class MonthlyActivityDto
{
    public int Month { get; set; }
    public int EpisodeCount { get; set; }
    public int MovieCount { get; set; }
    public int MinutesWatched { get; set; }
    public List<MonthlySeriesDto> Series { get; set; } = new();
    public List<MonthlyMovieDto> Movies { get; set; } = new();
}

public class MonthlySeriesDto
{
    public int MediaItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int EpisodeCount { get; set; }
    public List<MonthlyEpisodeDto> Episodes { get; set; } = new();
}

public class MonthlyEpisodeDto
{
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public string? EpisodeName { get; set; }
    public DateTime WatchedAt { get; set; }
}

public class MonthlyMovieDto
{
    public int MediaItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public DateTime WatchedAt { get; set; }
    public decimal? UserRating { get; set; }
    public double? TmdbRating { get; set; }
    public string? ReleaseDate { get; set; }
}

public class TopSeriesDto
{
    public int MediaItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int EpisodesWatched { get; set; }
    public int MinutesWatched { get; set; }
    public decimal? UserRating { get; set; }
    public double? TmdbRating { get; set; }
}

public class TopMovieDto
{
    public int MediaItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? Runtime { get; set; }
    public decimal? UserRating { get; set; }
    public double? TmdbRating { get; set; }
    public string? ReleaseDate { get; set; }
    public DateTime WatchedAt { get; set; }
}

public class WrappedMediaDto
{
    public int MediaItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public string? ReleaseDate { get; set; }
    public decimal? UserRating { get; set; }
    public double? TmdbRating { get; set; }
}

public class TopNetworkDto
{
    public string Network { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class CalendarDayDto
{
    public string Date { get; set; } = string.Empty; // yyyy-MM-dd
    public List<CalendarEventDto> Events { get; set; } = new();
}

public class CalendarEventDto
{
    public int MediaItemId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty; // "movie" | "series"
    public string? EpisodeName { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public DateTime Timestamp { get; set; }
}

public class UpcomingEpisodeDto
{
    public int MediaItemId { get; set; }
    public string SeriesTitle { get; set; } = string.Empty;
    public int SeasonNumber { get; set; }
    public int EpisodeNumber { get; set; }
    public string? EpisodeName { get; set; }
    public string AirDate { get; set; } = string.Empty;
}
