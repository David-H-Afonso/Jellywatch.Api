using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class StatsController : BaseApiController
{
    private readonly JellywatchDbContext _context;

    public StatsController(JellywatchDbContext context)
    {
        _context = context;
    }

    [HttpGet("{profileId:int}/wrapped")]
    public async Task<ActionResult<WrappedDto>> GetWrapped(int profileId, [FromQuery] int? year)
    {
        var targetYear = year ?? DateTime.UtcNow.Year;
        var startDate = new DateTime(targetYear, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = new DateTime(targetYear + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // All "Finished" events for this profile in the target year
        var finishedEvents = await _context.WatchEvents
            .Where(e => e.ProfileId == profileId
                && e.EventType == WatchEventType.Finished
                && e.Timestamp >= startDate
                && e.Timestamp < endDate)
            .Select(e => new
            {
                e.Id,
                e.MediaItemId,
                e.EpisodeId,
                e.MovieId,
                e.Timestamp,
                MediaTitle = e.MediaItem.Title,
                MediaType = e.MediaItem.MediaType,
                EpisodeRuntime = e.Episode != null ? e.Episode.Runtime : null,
                EpisodeNumber = e.Episode != null ? (int?)e.Episode.EpisodeNumber : null,
                EpisodeName = e.Episode != null ? e.Episode.Name : null,
                SeasonNumber = e.Episode != null ? (int?)e.Episode.Season.SeasonNumber : null,
                MovieRuntime = e.Movie != null ? e.Movie.Runtime : null,
                MovieReleaseDate = e.Movie != null ? e.MediaItem.ReleaseDate : null,
                Network = e.MediaItem.Series != null ? e.MediaItem.Series.Network : null,
                MediaItemReleaseDate = e.MediaItem.ReleaseDate,
            })
            .ToListAsync();

        if (finishedEvents.Count == 0)
        {
            return Ok(new WrappedDto { Year = targetYear });
        }

        // Distinct episodes and movies
        var episodeEvents = finishedEvents.Where(e => e.EpisodeId != null).ToList();
        var movieEvents = finishedEvents.Where(e => e.MovieId != null).ToList();

        var uniqueEpisodeIds = episodeEvents.Select(e => e.EpisodeId!.Value).Distinct().ToList();
        var uniqueMovieIds = movieEvents.Select(e => e.MovieId!.Value).Distinct().ToList();
        var uniqueSeriesMediaItemIds = episodeEvents
            .Select(e => e.MediaItemId).Distinct().ToList();

        // Total minutes
        var episodeMinutes = episodeEvents
            .Where(e => e.EpisodeRuntime.HasValue)
            .Sum(e => e.EpisodeRuntime!.Value);
        var movieMinutes = movieEvents
            .Where(e => e.MovieRuntime.HasValue)
            .Sum(e => e.MovieRuntime!.Value);

        // Pre-fetch movie user ratings (needed for monthly + top movies)
        var movieRatings = await _context.ProfileWatchStates
            .Where(s => s.ProfileId == profileId
                && s.MovieId != null
                && uniqueMovieIds.Contains(s.MovieId!.Value))
            .Select(s => new { s.MovieId, s.UserRating })
            .ToListAsync();
        var ratingMap = movieRatings.ToDictionary(r => r.MovieId!.Value, r => r.UserRating);

        // Pre-fetch series user ratings
        var seriesRatings = await _context.ProfileWatchStates
            .Where(s => s.ProfileId == profileId
                && s.EpisodeId == null
                && s.MovieId == null
                && s.SeasonId == null
                && uniqueSeriesMediaItemIds.Contains(s.MediaItemId))
            .Select(s => new { s.MediaItemId, s.UserRating })
            .ToListAsync();
        var seriesRatingMap = seriesRatings.ToDictionary(r => r.MediaItemId, r => r.UserRating);

        // Pre-fetch TMDB ratings for all media items in this wrapped year
        var allMediaItemIds = finishedEvents.Select(e => e.MediaItemId).Distinct().ToList();
        var tmdbRatingsRaw = await _context.ExternalRatings
            .Where(er => allMediaItemIds.Contains(er.MediaItemId) && er.Provider == ExternalProvider.Tmdb)
            .Select(er => new { er.MediaItemId, er.Score })
            .ToListAsync();
        var tmdbRatingMap = tmdbRatingsRaw.ToDictionary(
            er => er.MediaItemId,
            er => double.TryParse(er.Score, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : (double?)null);
        var monthlyActivity = Enumerable.Range(1, 12).Select(month =>
        {
            var monthEpisodes = episodeEvents.Where(e => e.Timestamp.Month == month).ToList();
            var monthMovies = movieEvents.Where(e => e.Timestamp.Month == month).ToList();

            var seriesBreakdown = monthEpisodes
                .GroupBy(e => new { e.MediaItemId, e.MediaTitle })
                .Select(g => new MonthlySeriesDto
                {
                    MediaItemId = g.Key.MediaItemId,
                    Title = g.Key.MediaTitle,
                    EpisodeCount = g.Count(),
                    Episodes = g
                        .OrderBy(e => e.Timestamp)
                        .Select(e => new MonthlyEpisodeDto
                        {
                            SeasonNumber = e.SeasonNumber ?? 0,
                            EpisodeNumber = e.EpisodeNumber ?? 0,
                            EpisodeName = e.EpisodeName,
                            WatchedAt = e.Timestamp,
                        })
                        .ToList(),
                })
                .OrderByDescending(s => s.EpisodeCount)
                .Take(10)
                .ToList();

            var movieBreakdown = monthMovies
                .GroupBy(e => e.MediaItemId)
                .Select(g =>
                {
                    var first = g.First();
                    return new MonthlyMovieDto
                    {
                        MediaItemId = first.MediaItemId,
                        Title = first.MediaTitle,
                        WatchedAt = g.Max(e => e.Timestamp),
                        UserRating = ratingMap.GetValueOrDefault(first.MovieId ?? 0),
                        TmdbRating = tmdbRatingMap.GetValueOrDefault(first.MediaItemId),
                        ReleaseDate = first.MovieReleaseDate,
                    };
                })
                .ToList();

            return new MonthlyActivityDto
            {
                Month = month,
                EpisodeCount = monthEpisodes.Count,
                MovieCount = monthMovies.Count,
                MinutesWatched =
                    monthEpisodes.Where(e => e.EpisodeRuntime.HasValue).Sum(e => e.EpisodeRuntime!.Value)
                    + monthMovies.Where(e => e.MovieRuntime.HasValue).Sum(e => e.MovieRuntime!.Value),
                Series = seriesBreakdown,
                Movies = movieBreakdown,
            };
        }).ToList();

        // Most active month
        var mostActive = monthlyActivity.OrderByDescending(m => m.EpisodeCount + m.MovieCount).First();

        // Top series (by episodes watched)
        var topSeries = episodeEvents
            .GroupBy(e => new { e.MediaItemId, e.MediaTitle })
            .Select(g => new TopSeriesDto
            {
                MediaItemId = g.Key.MediaItemId,
                Title = g.Key.MediaTitle,
                EpisodesWatched = g.Count(),
                MinutesWatched = g.Where(e => e.EpisodeRuntime.HasValue).Sum(e => e.EpisodeRuntime!.Value),
                UserRating = seriesRatingMap.GetValueOrDefault(g.Key.MediaItemId),
                TmdbRating = tmdbRatingMap.GetValueOrDefault(g.Key.MediaItemId),
            })
            .OrderByDescending(s => s.EpisodesWatched)
            .Take(10)
            .ToList();

        // Top movies (by user rating desc, then TMDB rating desc, then title)
        var topMovies = movieEvents
            .GroupBy(e => new { e.MediaItemId, e.MediaTitle, e.MovieRuntime, MovieId = e.MovieId!.Value, e.MovieReleaseDate })
            .Select(g => new TopMovieDto
            {
                MediaItemId = g.Key.MediaItemId,
                Title = g.Key.MediaTitle,
                Runtime = g.Key.MovieRuntime,
                UserRating = ratingMap.GetValueOrDefault(g.Key.MovieId),
                TmdbRating = tmdbRatingMap.GetValueOrDefault(g.Key.MediaItemId),
                ReleaseDate = g.Key.MovieReleaseDate,
                WatchedAt = g.Max(e => e.Timestamp),
            })
            .OrderByDescending(m => m.UserRating.HasValue ? 1 : 0)
            .ThenByDescending(m => m.UserRating ?? 0)
            .ThenBy(m => m.Title)
            .Take(10)
            .ToList();

        // First watch of the year
        var firstEvent = finishedEvents.OrderBy(e => e.Timestamp).First();
        var firstWatch = new WrappedMediaDto
        {
            MediaItemId = firstEvent.MediaItemId,
            Title = firstEvent.MediaTitle,
            MediaType = firstEvent.MediaType == MediaType.Movie ? "movie" : "series",
            Timestamp = firstEvent.Timestamp,
            ReleaseDate = firstEvent.MediaItemReleaseDate,
            UserRating = firstEvent.MediaType == MediaType.Movie
                ? ratingMap.GetValueOrDefault(firstEvent.MovieId ?? 0)
                : seriesRatingMap.GetValueOrDefault(firstEvent.MediaItemId),
            TmdbRating = tmdbRatingMap.GetValueOrDefault(firstEvent.MediaItemId),
        };

        // Top networks
        var topNetworks = episodeEvents
            .Where(e => !string.IsNullOrWhiteSpace(e.Network))
            .GroupBy(e => e.Network!)
            .Select(g => new TopNetworkDto { Network = g.Key, Count = g.Count() })
            .OrderByDescending(n => n.Count)
            .Take(5)
            .ToList();

        // Days active, busiest day, longest streak
        var activeDates = finishedEvents
            .Select(e => e.Timestamp.Date)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var totalDaysActive = activeDates.Count;

        var busiestDay = finishedEvents
            .GroupBy(e => e.Timestamp.Date)
            .OrderByDescending(g => g.Count())
            .First();

        var longestStreak = 1;
        var currentStreak = 1;
        for (var i = 1; i < activeDates.Count; i++)
        {
            if ((activeDates[i] - activeDates[i - 1]).Days == 1)
            {
                currentStreak++;
                if (currentStreak > longestStreak) longestStreak = currentStreak;
            }
            else
            {
                currentStreak = 1;
            }
        }
        if (activeDates.Count <= 1) longestStreak = activeDates.Count;

        return Ok(new WrappedDto
        {
            Year = targetYear,
            TotalEpisodesWatched = uniqueEpisodeIds.Count,
            TotalMoviesWatched = uniqueMovieIds.Count,
            TotalSeriesWatched = uniqueSeriesMediaItemIds.Count,
            TotalMinutesWatched = episodeMinutes + movieMinutes,
            TotalDaysActive = totalDaysActive,
            LongestStreakDays = longestStreak,
            BusiestDay = busiestDay.Key.ToString("yyyy-MM-dd"),
            BusiestDayCount = busiestDay.Count(),
            MonthlyActivity = monthlyActivity,
            TopSeries = topSeries,
            TopMovies = topMovies,
            FirstWatch = firstWatch,
            MostActiveMonth = mostActive.Month.ToString(),
            MostActiveMonthCount = mostActive.EpisodeCount + mostActive.MovieCount,
            TopNetworks = topNetworks,
        });
    }

    [HttpGet("{profileId:int}/calendar")]
    public async Task<ActionResult<List<CalendarDayDto>>> GetCalendar(
        int profileId,
        [FromQuery] int? year,
        [FromQuery] int? month)
    {
        var targetYear = year ?? DateTime.UtcNow.Year;
        var targetMonth = month ?? DateTime.UtcNow.Month;
        var startDate = new DateTime(targetYear, targetMonth, 1, 0, 0, 0, DateTimeKind.Utc);
        var endDate = startDate.AddMonths(1);

        var events = await _context.WatchEvents
            .Where(e => e.ProfileId == profileId
                && e.EventType == WatchEventType.Finished
                && e.Timestamp >= startDate
                && e.Timestamp < endDate)
            .OrderBy(e => e.Timestamp)
            .Select(e => new
            {
                e.MediaItemId,
                MediaTitle = e.MediaItem.Title,
                MediaType = e.MediaItem.MediaType,
                EpisodeName = e.Episode != null ? e.Episode.Name : null,
                SeasonNumber = e.Episode != null ? (int?)e.Episode.Season.SeasonNumber : null,
                EpisodeNumber = e.Episode != null ? (int?)e.Episode.EpisodeNumber : null,
                e.Timestamp,
            })
            .ToListAsync();

        var grouped = events
            .GroupBy(e => e.Timestamp.Date)
            .Select(g => new CalendarDayDto
            {
                Date = g.Key.ToString("yyyy-MM-dd"),
                Events = g.Select(e => new CalendarEventDto
                {
                    MediaItemId = e.MediaItemId,
                    Title = e.MediaTitle,
                    MediaType = e.MediaType == MediaType.Movie ? "movie" : "series",
                    EpisodeName = e.EpisodeName,
                    SeasonNumber = e.SeasonNumber,
                    EpisodeNumber = e.EpisodeNumber,
                    Timestamp = e.Timestamp,
                }).ToList(),
            })
            .ToList();

        return Ok(grouped);
    }

    [HttpGet("{profileId:int}/upcoming")]
    public async Task<ActionResult<List<UpcomingEpisodeDto>>> GetUpcoming(int profileId, [FromQuery] int days = 30)
    {
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        var endDate = DateTime.UtcNow.Date.AddDays(days).ToString("yyyy-MM-dd");

        // Get series that the profile has any interaction with:
        // series-level states, season-level states, episode-level states, or watch events
        var seriesFromStates = await _context.ProfileWatchStates
            .Where(s => s.ProfileId == profileId
                && (s.State == WatchState.InProgress || s.State == WatchState.Seen))
            .Select(s => s.MediaItemId)
            .Distinct()
            .ToListAsync();

        var seriesFromEvents = await _context.WatchEvents
            .Where(e => e.ProfileId == profileId && e.EpisodeId != null)
            .Select(e => e.MediaItemId)
            .Distinct()
            .ToListAsync();

        var watchedSeriesMediaItemIds = seriesFromStates
            .Union(seriesFromEvents)
            .Distinct()
            .ToList();

        if (watchedSeriesMediaItemIds.Count == 0)
            return Ok(new List<UpcomingEpisodeDto>());

        var upcoming = await _context.Set<Domain.Episode>()
            .Where(ep => ep.AirDate != null
                && string.Compare(ep.AirDate, today) >= 0
                && string.Compare(ep.AirDate, endDate) <= 0
                && watchedSeriesMediaItemIds.Contains(ep.Season.Series.MediaItemId)
                && !_context.ProfileMediaBlocks.Any(b => b.ProfileId == profileId && b.MediaItemId == ep.Season.Series.MediaItemId))
            .OrderBy(ep => ep.AirDate)
            .ThenBy(ep => ep.Season.Series.MediaItem.Title)
            .Select(ep => new UpcomingEpisodeDto
            {
                MediaItemId = ep.Season.Series.MediaItemId,
                SeriesTitle = ep.Season.Series.MediaItem.Title,
                SeasonNumber = ep.Season.SeasonNumber,
                EpisodeNumber = ep.EpisodeNumber,
                EpisodeName = ep.Name,
                AirDate = ep.AirDate!,
            })
            .Take(50)
            .ToListAsync();

        return Ok(upcoming);
    }
}
