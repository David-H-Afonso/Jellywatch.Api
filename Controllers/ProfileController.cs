using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Common;
using Jellywatch.Api.Infrastructure.Persistence;
using Jellywatch.Api.Application.Services;
using Jellywatch.Api.Infrastructure.BackgroundJobs;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class ProfileController : BaseApiController
{
    private readonly JellywatchDbContext _context;
    private readonly IPropagationService _propagationService;
    private readonly IWatchStateService _watchStateService;

    public ProfileController(JellywatchDbContext context, IPropagationService propagationService, IWatchStateService watchStateService)
    {
        _context = context;
        _propagationService = propagationService;
        _watchStateService = watchStateService;
    }

    [HttpGet]
    public async Task<ActionResult<List<ProfileDto>>> GetProfiles()
    {
        var userId = CurrentUserId;

        // Get user's profiles + joint profiles they're linked to
        var directProfiles = await _context.Profiles
            .Where(p => p.UserId == userId)
            .ToListAsync();

        var linkedJointProfileIds = await _context.PropagationRules
            .Where(r => r.IsActive && r.TargetProfile.UserId == userId)
            .Select(r => r.SourceProfileId)
            .Distinct()
            .ToListAsync();

        var jointProfiles = await _context.Profiles
            .Where(p => linkedJointProfileIds.Contains(p.Id) && !directProfiles.Select(d => d.Id).Contains(p.Id))
            .ToListAsync();

        var allProfiles = directProfiles.Concat(jointProfiles).ToList();

        var dtos = allProfiles.Select(p => new ProfileDto
        {
            Id = p.Id,
            DisplayName = p.DisplayName,
            JellyfinUserId = p.JellyfinUserId,
            IsJoint = p.IsJoint,
            UserId = p.UserId,
            CreatedAt = p.CreatedAt
        }).ToList();

        return Ok(dtos);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProfileDetailDto>> GetProfileDetail(int id)
    {
        var profile = await _context.Profiles.FindAsync(id);
        if (profile is null)
            return NotFound(new { message = "Profile not found" });

        var seriesWatching = await _context.Series
            .Where(s => s.Seasons.Any(sea => sea.Episodes.Any()))
            .Where(s =>
                s.Seasons.SelectMany(sea => sea.Episodes)
                    .Any(ep => ep.WatchStates.Any(ws => ws.ProfileId == id && ws.State == WatchState.Seen))
                &&
                s.Seasons.SelectMany(sea => sea.Episodes)
                    .Any(ep => !ep.WatchStates.Any(ws => ws.ProfileId == id
                        && (ws.State == WatchState.Seen || ws.State == WatchState.WontWatch))))
            .CountAsync();

        var seriesCompleted = await CountCompletedSeriesAsync(id);

        var moviesSeen = await _context.ProfileWatchStates
            .Where(ws => ws.ProfileId == id && ws.MovieId.HasValue && ws.State == WatchState.Seen)
            .CountAsync();

        var episodesSeen = await _context.ProfileWatchStates
            .Where(ws => ws.ProfileId == id && ws.EpisodeId.HasValue && ws.State == WatchState.Seen)
            .CountAsync();

        return Ok(new ProfileDetailDto
        {
            Id = profile.Id,
            DisplayName = profile.DisplayName,
            JellyfinUserId = profile.JellyfinUserId,
            IsJoint = profile.IsJoint,
            UserId = profile.UserId,
            CreatedAt = profile.CreatedAt,
            TotalSeriesWatching = seriesWatching,
            TotalSeriesCompleted = seriesCompleted,
            TotalMoviesSeen = moviesSeen,
            TotalEpisodesSeen = episodesSeen
        });
    }

    [HttpGet("{profileId:int}/activity")]
    public async Task<ActionResult<PagedResult<ActivityDto>>> GetActivity(int profileId, [FromQuery] ActivityQueryParameters query)
    {
        var baseQuery = _context.WatchEvents
            .Where(e => e.ProfileId == profileId)
            .Include(e => e.MediaItem)
            .Include(e => e.Episode)
                .ThenInclude(ep => ep!.Season)
            .AsQueryable();

        // Filter by media type
        if (!string.IsNullOrWhiteSpace(query.MediaType))
        {
            if (query.MediaType.Equals("movie", StringComparison.OrdinalIgnoreCase))
                baseQuery = baseQuery.Where(e => e.MediaItem.MediaType == Domain.Enums.MediaType.Movie);
            else if (query.MediaType.Equals("series", StringComparison.OrdinalIgnoreCase))
                baseQuery = baseQuery.Where(e => e.MediaItem.MediaType == Domain.Enums.MediaType.Series);
        }

        // Filter by specific media item
        if (query.MediaItemId.HasValue)
            baseQuery = baseQuery.Where(e => e.MediaItemId == query.MediaItemId.Value);

        // Filter by date range
        if (query.DateFrom.HasValue)
            baseQuery = baseQuery.Where(e => e.Timestamp >= query.DateFrom.Value);
        if (query.DateTo.HasValue)
            baseQuery = baseQuery.Where(e => e.Timestamp < query.DateTo.Value.AddDays(1));

        // Search by title/episode name
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.ToLower();
            baseQuery = baseQuery.Where(e =>
                e.MediaItem.Title.ToLower().Contains(search)
                || (e.Episode != null && e.Episode.Name != null && e.Episode.Name.ToLower().Contains(search)));
        }

        // Sort
        baseQuery = query.SortBy?.ToLower() switch
        {
            "oldest" => baseQuery.OrderBy(e => e.Timestamp),
            _ => baseQuery.OrderByDescending(e => e.Timestamp),
        };

        var totalCount = await baseQuery.CountAsync();

        var events = await baseQuery
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync();

        // Batch-load user ratings for these events.
        // Series activity is episode-based, so prefer episode rating, then season, then series.
        var episodeIds = events.Where(e => e.EpisodeId != null).Select(e => e.EpisodeId!.Value).Distinct().ToList();
        var seasonIds = events.Where(e => e.Episode != null).Select(e => e.Episode!.SeasonId).Distinct().ToList();
        var seriesMediaItemIds = events
            .Where(e => e.MediaItem.MediaType == Domain.Enums.MediaType.Series)
            .Select(e => e.MediaItemId)
            .Distinct()
            .ToList();
        var movieMediaItemIds = events
            .Where(e => e.MediaItem.MediaType == Domain.Enums.MediaType.Movie)
            .Select(e => e.MediaItemId)
            .Distinct()
            .ToList();

        var seriesIdByMediaItemId = new Dictionary<int, int>();
        if (seriesMediaItemIds.Count > 0)
        {
            seriesIdByMediaItemId = await _context.Series
                .Where(s => seriesMediaItemIds.Contains(s.MediaItemId))
                .ToDictionaryAsync(s => s.MediaItemId, s => s.Id);
        }

        var movieIdByMediaItemId = new Dictionary<int, int>();
        if (movieMediaItemIds.Count > 0)
        {
            movieIdByMediaItemId = await _context.Movies
                .Where(m => movieMediaItemIds.Contains(m.MediaItemId))
                .ToDictionaryAsync(m => m.MediaItemId, m => m.Id);
        }

        var movieIds = events
            .Where(e => e.MovieId != null)
            .Select(e => e.MovieId!.Value)
            .Concat(movieIdByMediaItemId.Values)
            .Distinct()
            .ToList();
        var mediaItemIds = seriesMediaItemIds
            .Concat(movieMediaItemIds)
            .Distinct()
            .ToList();

        var tmdbRatingsByMediaItemId = new Dictionary<int, double?>();
        if (mediaItemIds.Count > 0)
        {
            var tmdbRatings = await _context.ExternalRatings
                .Where(r => mediaItemIds.Contains(r.MediaItemId) && r.Provider == ExternalProvider.Tmdb)
                .ToListAsync();
            tmdbRatingsByMediaItemId = tmdbRatings.ToDictionary(
                r => r.MediaItemId,
                r => TryParseRating(r.Score));
        }

        var episodeRatings = new Dictionary<int, decimal?>();
        if (episodeIds.Count > 0)
        {
            var episodeStates = await _context.ProfileWatchStates
                .Where(s => s.ProfileId == profileId && s.EpisodeId != null && episodeIds.Contains(s.EpisodeId.Value))
                .ToListAsync();
            episodeRatings = episodeStates
                .GroupBy(s => s.EpisodeId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.LastUpdated).First().UserRating);
        }

        var seasonRatings = new Dictionary<int, decimal?>();
        if (seasonIds.Count > 0)
        {
            var seasonStates = await _context.ProfileWatchStates
                .Where(s => s.ProfileId == profileId && s.SeasonId != null && seasonIds.Contains(s.SeasonId.Value))
                .ToListAsync();
            seasonRatings = seasonStates
                .GroupBy(s => s.SeasonId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.LastUpdated).First().UserRating);
        }

        var seriesRatings = new Dictionary<int, decimal?>();
        if (seriesMediaItemIds.Count > 0)
        {
            var seriesStates = await _context.ProfileWatchStates
                .Where(s => s.ProfileId == profileId
                    && seriesMediaItemIds.Contains(s.MediaItemId)
                    && s.EpisodeId == null
                    && s.SeasonId == null
                    && s.MovieId == null)
                .ToListAsync();
            seriesRatings = seriesStates
                .GroupBy(s => s.MediaItemId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.LastUpdated).First().UserRating);
        }

        var movieRatings = new Dictionary<int, decimal?>();
        var movieRatingsByMediaItemId = new Dictionary<int, decimal?>();
        if (movieIds.Count > 0 || movieMediaItemIds.Count > 0)
        {
            var movieStates = await _context.ProfileWatchStates
                .Where(s => s.ProfileId == profileId
                    && s.EpisodeId == null
                    && s.SeasonId == null
                    && ((s.MovieId != null && movieIds.Contains(s.MovieId.Value))
                        || movieMediaItemIds.Contains(s.MediaItemId)))
                .ToListAsync();
            movieRatings = movieStates
                .Where(s => s.MovieId != null)
                .GroupBy(s => s.MovieId!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.LastUpdated).First().UserRating);
            movieRatingsByMediaItemId = movieStates
                .GroupBy(s => s.MediaItemId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.LastUpdated).First().UserRating);
        }

        var dtos = events.Select(e =>
        {
            decimal? userRating = null;
            int? seriesId = e.Episode?.Season?.SeriesId;
            if (seriesId == null
                && e.MediaItem.MediaType == Domain.Enums.MediaType.Series
                && seriesIdByMediaItemId.TryGetValue(e.MediaItemId, out var resolvedSeriesId))
            {
                seriesId = resolvedSeriesId;
            }

            int? movieId = e.MovieId;
            if (movieId == null
                && e.MediaItem.MediaType == Domain.Enums.MediaType.Movie
                && movieIdByMediaItemId.TryGetValue(e.MediaItemId, out var resolvedMovieId))
            {
                movieId = resolvedMovieId;
            }

            if (e.EpisodeId != null)
            {
                episodeRatings.TryGetValue(e.EpisodeId.Value, out var episodeRating);
                decimal? seasonRating = null;
                if (e.Episode != null)
                    seasonRatings.TryGetValue(e.Episode.SeasonId, out seasonRating);
                seriesRatings.TryGetValue(e.MediaItemId, out var seriesRating);
                userRating = episodeRating ?? seasonRating ?? seriesRating;
            }
            else if (e.MediaItem.MediaType == Domain.Enums.MediaType.Movie)
            {
                decimal? movieRating = null;
                if (movieId.HasValue)
                    movieRatings.TryGetValue(movieId.Value, out movieRating);
                movieRatingsByMediaItemId.TryGetValue(e.MediaItemId, out var movieMediaItemRating);
                userRating = movieRating ?? movieMediaItemRating;
            }
            else if (e.MediaItem.MediaType == Domain.Enums.MediaType.Series)
                seriesRatings.TryGetValue(e.MediaItemId, out userRating);

            tmdbRatingsByMediaItemId.TryGetValue(e.MediaItemId, out var mediaItemTmdbRating);

            return new ActivityDto
            {
                Id = e.Id,
                MediaItemId = e.MediaItemId,
                SeriesId = seriesId,
                MovieId = movieId,
                MediaTitle = e.MediaItem.Title,
                EpisodeName = e.Episode?.Name,
                EpisodeNumber = e.Episode?.EpisodeNumber,
                SeasonNumber = e.Episode?.Season?.SeasonNumber,
                MediaType = e.MediaItem.MediaType,
                EventType = e.EventType,
                Source = e.Source,
                Timestamp = e.Timestamp,
                CreatedAt = e.CreatedAt,
                PosterPath = e.MediaItem.PosterPath,
                UserRating = userRating,
                TmdbRating = e.Episode?.TmdbRating ?? mediaItemTmdbRating,
            };
        }).ToList();

        return Ok(new PagedResult<ActivityDto>
        {
            Data = dtos,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        });
    }

    [HttpPatch("{profileId:int}/episodes/{episodeId:int}/state")]
    public async Task<IActionResult> UpdateEpisodeState(int profileId, int episodeId, [FromBody] WatchStateUpdateDto dto)
    {
        var (success, message, error) = await _watchStateService.UpdateEpisodeStateAsync(profileId, episodeId, dto);
        if (!success) return NotFound(new { message = error });
        return Ok(new { message, state = dto.State.ToString() });
    }

    [HttpPatch("{profileId:int}/movies/{movieId:int}/state")]
    public async Task<IActionResult> UpdateMovieState(int profileId, int movieId, [FromBody] WatchStateUpdateDto dto)
    {
        var (success, message, error) = await _watchStateService.UpdateMovieStateAsync(profileId, movieId, dto);
        if (!success) return NotFound(new { message = error });
        return Ok(new { message, state = dto.State.ToString() });
    }

    [HttpPatch("{profileId:int}/seasons/{seasonId:int}/state")]
    public async Task<IActionResult> UpdateSeasonState(int profileId, int seasonId, [FromBody] WatchStateUpdateDto dto)
    {
        var (success, message, error) = await _watchStateService.UpdateSeasonStateAsync(profileId, seasonId, dto);
        if (!success) return NotFound(new { message = error });
        return Ok(new { message });
    }

    [HttpPatch("{profileId:int}/series/{seriesId:int}/state")]
    public async Task<IActionResult> UpdateSeriesState(int profileId, int seriesId, [FromBody] WatchStateUpdateDto dto)
    {
        var (success, message, error) = await _watchStateService.UpdateSeriesStateAsync(profileId, seriesId, dto);
        if (!success) return NotFound(new { message = error });
        return Ok(new { message });
    }

    private static double? TryParseRating(string? score)
    {
        if (string.IsNullOrWhiteSpace(score)) return null;
        return double.TryParse(score, NumberStyles.Float, CultureInfo.InvariantCulture, out var rating)
            ? rating
            : null;
    }

    private async Task<int> CountCompletedSeriesAsync(int profileId)
    {
        // A series is "completed" if all its episodes are Seen or WontWatch (and at least one is Seen)
        var seriesIds = await _context.Series
            .Where(s => s.Seasons.Any(sea => sea.Episodes.Any()))
            .Where(s => s.Seasons.SelectMany(sea => sea.Episodes)
                .All(ep => ep.WatchStates.Any(ws => ws.ProfileId == profileId
                    && (ws.State == WatchState.Seen || ws.State == WatchState.WontWatch))))
            .Where(s => s.Seasons.SelectMany(sea => sea.Episodes)
                .Any(ep => ep.WatchStates.Any(ws => ws.ProfileId == profileId && ws.State == WatchState.Seen)))
            .Select(s => s.Id)
            .ToListAsync();

        return seriesIds.Count;
    }

    [HttpPost("{profileId:int}/media/{mediaItemId:int}")]
    public async Task<IActionResult> AddMediaToProfile(int profileId, int mediaItemId)
    {
        var profile = await _context.Profiles.FindAsync(profileId);
        if (profile is null) return NotFound(new { message = "Profile not found" });
        var isAdmin = (await _context.Users.FindAsync(CurrentUserId))?.IsAdmin == true;
        if (!isAdmin && profile.UserId != CurrentUserId)
            return Forbid();

        var mediaItem = await _context.MediaItems
            .Include(m => m.Series)
            .Include(m => m.Movie)
            .FirstOrDefaultAsync(m => m.Id == mediaItemId);
        if (mediaItem is null) return NotFound(new { message = "Media not found" });

        var block = await _context.ProfileMediaBlocks
            .FirstOrDefaultAsync(b => b.ProfileId == profileId && b.MediaItemId == mediaItemId);
        if (block is not null)
            _context.ProfileMediaBlocks.Remove(block);

        if (mediaItem.MediaType == MediaType.Series)
        {
            var exists = await _context.ProfileWatchStates.AnyAsync(ws =>
                ws.ProfileId == profileId
                && ws.MediaItemId == mediaItemId
                && ws.EpisodeId == null
                && ws.SeasonId == null
                && ws.MovieId == null);

            if (!exists)
            {
                _context.ProfileWatchStates.Add(new ProfileWatchState
                {
                    ProfileId = profileId,
                    MediaItemId = mediaItemId,
                    State = WatchState.Unseen,
                    LastUpdated = DateTime.UtcNow
                });
            }
        }
        else
        {
            if (mediaItem.Movie is null) return NotFound(new { message = "Movie not found" });

            var exists = await _context.ProfileWatchStates.AnyAsync(ws =>
                ws.ProfileId == profileId
                && ws.MediaItemId == mediaItemId
                && ws.MovieId == mediaItem.Movie.Id);

            if (!exists)
            {
                _context.ProfileWatchStates.Add(new ProfileWatchState
                {
                    ProfileId = profileId,
                    MediaItemId = mediaItemId,
                    MovieId = mediaItem.Movie.Id,
                    State = WatchState.Unseen,
                    LastUpdated = DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPatch("{profileId:int}/series/{seriesId:int}/dashboard")]
    public async Task<IActionResult> UpdateSeriesDashboardPreference(int profileId, int seriesId, [FromBody] DashboardPreferenceUpdateDto dto)
    {
        var profile = await _context.Profiles.FindAsync(profileId);
        if (profile is null) return NotFound(new { message = "Profile not found" });
        var isAdmin = (await _context.Users.FindAsync(CurrentUserId))?.IsAdmin == true;
        if (!isAdmin && profile.UserId != CurrentUserId)
            return Forbid();

        var series = await _context.Series.FirstOrDefaultAsync(s => s.Id == seriesId);
        if (series is null) return NotFound(new { message = "Series not found" });

        var watchState = await _context.ProfileWatchStates
            .FirstOrDefaultAsync(ws => ws.ProfileId == profileId
                && ws.MediaItemId == series.MediaItemId
                && ws.EpisodeId == null
                && ws.SeasonId == null
                && ws.MovieId == null);

        if (watchState is null)
        {
            watchState = new ProfileWatchState
            {
                ProfileId = profileId,
                MediaItemId = series.MediaItemId,
                State = WatchState.Unseen,
                LastUpdated = DateTime.UtcNow
            };
            _context.ProfileWatchStates.Add(watchState);
        }

        watchState.IncludeInDashboard = dto.IncludeInDashboard;
        watchState.ExcludeFromDashboard = !dto.IncludeInDashboard;
        await _context.SaveChangesAsync();

        return Ok(new
        {
            includeInDashboard = watchState.IncludeInDashboard,
            excludeFromDashboard = watchState.ExcludeFromDashboard,
            isInDashboard = watchState.IncludeInDashboard
        });
    }

    private async Task RemoveMediaFromProfileAsync(int profileId, int mediaItemId)
    {
        var watchStates = await _context.ProfileWatchStates
            .Where(ws => ws.ProfileId == profileId && ws.MediaItemId == mediaItemId)
            .ToListAsync();
        _context.ProfileWatchStates.RemoveRange(watchStates);

        var watchEvents = await _context.WatchEvents
            .Where(e => e.ProfileId == profileId && e.MediaItemId == mediaItemId)
            .ToListAsync();
        _context.WatchEvents.RemoveRange(watchEvents);

        await _context.SaveChangesAsync();
    }

    [HttpDelete("{profileId:int}/media/{mediaItemId:int}")]
    public async Task<IActionResult> RemoveMediaFromProfile(int profileId, int mediaItemId)
    {
        // Only the profile owner (or admin) can remove media from their profile
        var profile = await _context.Profiles.FindAsync(profileId);
        if (profile is null) return NotFound(new { message = "Profile not found" });
        var isAdmin = (await _context.Users.FindAsync(CurrentUserId))?.IsAdmin == true;
        if (!isAdmin && profile.UserId != CurrentUserId)
            return Forbid();

        await RemoveMediaFromProfileAsync(profileId, mediaItemId);
        return NoContent();
    }

    [HttpPost("{profileId:int}/media/{mediaItemId:int}/block")]
    public async Task<IActionResult> BlockMediaForProfile(int profileId, int mediaItemId)
    {
        var profile = await _context.Profiles.FindAsync(profileId);
        if (profile is null) return NotFound(new { message = "Profile not found" });
        var isAdmin = (await _context.Users.FindAsync(CurrentUserId))?.IsAdmin == true;
        if (!isAdmin && profile.UserId != CurrentUserId)
            return Forbid();

        var mediaItem = await _context.MediaItems.FindAsync(mediaItemId);
        if (mediaItem is null) return NotFound(new { message = "Media not found" });

        // Remove from profile first
        await RemoveMediaFromProfileAsync(profileId, mediaItemId);

        // Add block if not already blocked
        var existing = await _context.ProfileMediaBlocks
            .FirstOrDefaultAsync(b => b.ProfileId == profileId && b.MediaItemId == mediaItemId);
        if (existing is null)
        {
            _context.ProfileMediaBlocks.Add(new Domain.Entities.ProfileMediaBlock
            {
                ProfileId = profileId,
                MediaItemId = mediaItemId,
            });
            await _context.SaveChangesAsync();
        }

        return Ok(new { message = "Media blocked for profile" });
    }

    [HttpDelete("{profileId:int}/media/{mediaItemId:int}/block")]
    public async Task<IActionResult> UnblockMediaForProfile(int profileId, int mediaItemId)
    {
        var profile = await _context.Profiles.FindAsync(profileId);
        if (profile is null) return NotFound(new { message = "Profile not found" });
        var isAdmin = (await _context.Users.FindAsync(CurrentUserId))?.IsAdmin == true;
        if (!isAdmin && profile.UserId != CurrentUserId)
            return Forbid();

        var block = await _context.ProfileMediaBlocks
            .FirstOrDefaultAsync(b => b.ProfileId == profileId && b.MediaItemId == mediaItemId);
        if (block is not null)
        {
            _context.ProfileMediaBlocks.Remove(block);
            await _context.SaveChangesAsync();
        }

        return NoContent();
    }

    [HttpGet("{profileId:int}/blocks")]
    public async Task<IActionResult> GetProfileBlocks(int profileId)
    {
        var profile = await _context.Profiles.FindAsync(profileId);
        if (profile is null) return NotFound(new { message = "Profile not found" });
        var isAdmin = (await _context.Users.FindAsync(CurrentUserId))?.IsAdmin == true;
        if (!isAdmin && profile.UserId != CurrentUserId)
            return Forbid();

        var blocks = await _context.ProfileMediaBlocks
            .Where(b => b.ProfileId == profileId)
            .Include(b => b.MediaItem)
                .ThenInclude(m => m.Translations)
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new ProfileBlockedItemDto
            {
                Id = b.Id,
                MediaItemId = b.MediaItemId,
                Title = b.MediaItem.Title,
                SpanishTitle = b.MediaItem.Translations
                    .Where(t => t.Language.StartsWith("es") && t.Title != null)
                    .Select(t => t.Title)
                    .FirstOrDefault(),
                MediaType = b.MediaItem.MediaType,
                BlockedAt = b.CreatedAt,
            })
            .ToListAsync();

        return Ok(blocks);
    }
}
