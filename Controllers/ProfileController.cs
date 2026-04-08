using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Helpers;
using Jellywatch.Api.Infrastructure;
using Jellywatch.Api.Services.Sync;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class ProfileController : BaseApiController
{
    private readonly JellywatchDbContext _context;
    private readonly IPropagationService _propagationService;

    public ProfileController(JellywatchDbContext context, IPropagationService propagationService)
    {
        _context = context;
        _propagationService = propagationService;
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

        // Batch-load user ratings for these events
        var episodeIds = events.Where(e => e.EpisodeId != null).Select(e => e.EpisodeId!.Value).Distinct().ToList();
        var movieIds = events.Where(e => e.MovieId != null).Select(e => e.MovieId!.Value).Distinct().ToList();

        var episodeRatings = episodeIds.Count > 0
            ? await _context.ProfileWatchStates
                .Where(s => s.ProfileId == profileId && s.EpisodeId != null && episodeIds.Contains(s.EpisodeId.Value))
                .ToDictionaryAsync(s => s.EpisodeId!.Value, s => s.UserRating)
            : new Dictionary<int, decimal?>();

        var movieRatings = movieIds.Count > 0
            ? await _context.ProfileWatchStates
                .Where(s => s.ProfileId == profileId && s.MovieId != null && movieIds.Contains(s.MovieId.Value))
                .ToDictionaryAsync(s => s.MovieId!.Value, s => s.UserRating)
            : new Dictionary<int, decimal?>();

        var dtos = events.Select(e =>
        {
            decimal? userRating = null;
            if (e.EpisodeId != null) episodeRatings.TryGetValue(e.EpisodeId.Value, out userRating);
            else if (e.MovieId != null) movieRatings.TryGetValue(e.MovieId.Value, out userRating);

            return new ActivityDto
            {
                Id = e.Id,
                MediaItemId = e.MediaItemId,
                SeriesId = e.Episode?.Season?.SeriesId,
                MovieId = e.MovieId,
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
                TmdbRating = e.Episode?.TmdbRating,
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
        var episode = await _context.Episodes
            .Include(e => e.Season)
            .FirstOrDefaultAsync(e => e.Id == episodeId);

        if (episode is null)
            return NotFound(new { message = "Episode not found" });

        var series = await _context.Series.FirstOrDefaultAsync(s => s.Id == episode.Season.SeriesId);
        if (series is null)
            return NotFound(new { message = "Series not found" });

        var watchState = await _context.ProfileWatchStates
            .FirstOrDefaultAsync(ws => ws.ProfileId == profileId && ws.EpisodeId == episodeId);

        if (watchState is null)
        {
            watchState = new ProfileWatchState
            {
                ProfileId = profileId,
                MediaItemId = series.MediaItemId,
                EpisodeId = episodeId,
                State = dto.State,
                IsManualOverride = true,
                LastUpdated = DateTime.UtcNow
            };
            _context.ProfileWatchStates.Add(watchState);
        }
        else
        {
            watchState.State = dto.State;
            watchState.IsManualOverride = true;
            watchState.LastUpdated = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        if (dto.State == WatchState.Seen)
        {
            _context.WatchEvents.Add(new WatchEvent
            {
                ProfileId = profileId,
                MediaItemId = series.MediaItemId,
                EpisodeId = episodeId,
                EventType = WatchEventType.Finished,
                Source = SyncSource.Manual,
                Timestamp = dto.Timestamp?.ToUniversalTime() ?? DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }
        else if (dto.State == WatchState.Unseen)
        {
            var lastFinished = await _context.WatchEvents
                .Where(e => e.ProfileId == profileId && e.EpisodeId == episodeId && e.EventType == WatchEventType.Finished)
                .OrderByDescending(e => e.Timestamp)
                .FirstOrDefaultAsync();
            if (lastFinished != null)
            {
                _context.WatchEvents.Remove(lastFinished);
                await _context.SaveChangesAsync();
            }
        }

        // Propagate if this profile is a joint profile
        await _propagationService.PropagateStateChangeAsync(profileId, series.MediaItemId, episodeId, null, dto.State, dto.Timestamp?.ToUniversalTime());

        return Ok(new { message = "Episode state updated", state = dto.State.ToString() });
    }

    [HttpPatch("{profileId:int}/movies/{movieId:int}/state")]
    public async Task<IActionResult> UpdateMovieState(int profileId, int movieId, [FromBody] WatchStateUpdateDto dto)
    {
        var movie = await _context.Movies.FindAsync(movieId);
        if (movie is null)
            return NotFound(new { message = "Movie not found" });

        var watchState = await _context.ProfileWatchStates
            .FirstOrDefaultAsync(ws => ws.ProfileId == profileId && ws.MovieId == movieId);

        if (watchState is null)
        {
            watchState = new ProfileWatchState
            {
                ProfileId = profileId,
                MediaItemId = movie.MediaItemId,
                MovieId = movieId,
                State = dto.State,
                IsManualOverride = true,
                LastUpdated = DateTime.UtcNow
            };
            _context.ProfileWatchStates.Add(watchState);
        }
        else
        {
            watchState.State = dto.State;
            watchState.IsManualOverride = true;
            watchState.LastUpdated = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        if (dto.State == WatchState.Seen)
        {
            _context.WatchEvents.Add(new WatchEvent
            {
                ProfileId = profileId,
                MediaItemId = movie.MediaItemId,
                MovieId = movieId,
                EventType = WatchEventType.Finished,
                Source = SyncSource.Manual,
                Timestamp = dto.Timestamp?.ToUniversalTime() ?? DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }
        else if (dto.State == WatchState.Unseen)
        {
            var lastFinished = await _context.WatchEvents
                .Where(e => e.ProfileId == profileId && e.MovieId == movieId && e.EventType == WatchEventType.Finished)
                .OrderByDescending(e => e.Timestamp)
                .FirstOrDefaultAsync();
            if (lastFinished != null)
            {
                _context.WatchEvents.Remove(lastFinished);
                await _context.SaveChangesAsync();
            }
        }

        await _propagationService.PropagateStateChangeAsync(profileId, movie.MediaItemId, null, movieId, dto.State, dto.Timestamp?.ToUniversalTime());

        return Ok(new { message = "Movie state updated", state = dto.State.ToString() });
    }

    [HttpPatch("{profileId:int}/seasons/{seasonId:int}/state")]
    public async Task<IActionResult> UpdateSeasonState(int profileId, int seasonId, [FromBody] WatchStateUpdateDto dto)
    {
        var season = await _context.Seasons
            .Include(s => s.Episodes)
            .Include(s => s.Series)
            .FirstOrDefaultAsync(s => s.Id == seasonId);

        if (season == null)
            return NotFound(new { message = "Season not found" });

        foreach (var episode in season.Episodes)
        {
            var ws = await _context.ProfileWatchStates
                .FirstOrDefaultAsync(s => s.ProfileId == profileId && s.EpisodeId == episode.Id);

            if (ws == null)
            {
                _context.ProfileWatchStates.Add(new ProfileWatchState
                {
                    ProfileId = profileId,
                    MediaItemId = season.Series.MediaItemId,
                    EpisodeId = episode.Id,
                    State = dto.State,
                    IsManualOverride = true,
                    LastUpdated = DateTime.UtcNow,
                });
            }
            else
            {
                ws.State = dto.State;
                ws.IsManualOverride = true;
                ws.LastUpdated = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();

        if (dto.State == WatchState.Seen)
        {
            var watchEvents = season.Episodes.Select(ep => new WatchEvent
            {
                ProfileId = profileId,
                MediaItemId = season.Series.MediaItemId,
                EpisodeId = ep.Id,
                EventType = WatchEventType.Finished,
                Source = SyncSource.Manual,
                Timestamp = dto.Timestamp?.ToUniversalTime() ?? DateTime.UtcNow
            });
            _context.WatchEvents.AddRange(watchEvents);
            await _context.SaveChangesAsync();
        }
        else if (dto.State == WatchState.Unseen)
        {
            var episodeIds = season.Episodes.Select(ep => ep.Id).ToList();
            var lastFinishedEvents = await _context.WatchEvents
                .Where(e => e.ProfileId == profileId && e.EpisodeId.HasValue && episodeIds.Contains(e.EpisodeId.Value) && e.EventType == WatchEventType.Finished)
                .GroupBy(e => e.EpisodeId)
                .Select(g => g.OrderByDescending(e => e.Timestamp).First())
                .ToListAsync();
            if (lastFinishedEvents.Count > 0)
            {
                _context.WatchEvents.RemoveRange(lastFinishedEvents);
                await _context.SaveChangesAsync();
            }
        }

        foreach (var episode in season.Episodes)
            await _propagationService.PropagateStateChangeAsync(profileId, season.Series.MediaItemId, episode.Id, null, dto.State, dto.Timestamp?.ToUniversalTime());

        return Ok(new { message = $"Season {seasonId} state updated to {dto.State}" });
    }

    [HttpPatch("{profileId:int}/series/{seriesId:int}/state")]
    public async Task<IActionResult> UpdateSeriesState(int profileId, int seriesId, [FromBody] WatchStateUpdateDto dto)
    {
        var series = await _context.Series
            .Include(s => s.Seasons)
                .ThenInclude(se => se.Episodes)
            .FirstOrDefaultAsync(s => s.Id == seriesId);

        if (series == null)
            return NotFound(new { message = "Series not found" });

        var allEpisodes = series.Seasons.SelectMany(s => s.Episodes).ToList();

        foreach (var episode in allEpisodes)
        {
            var ws = await _context.ProfileWatchStates
                .FirstOrDefaultAsync(s => s.ProfileId == profileId && s.EpisodeId == episode.Id);

            if (ws == null)
            {
                _context.ProfileWatchStates.Add(new ProfileWatchState
                {
                    ProfileId = profileId,
                    MediaItemId = series.MediaItemId,
                    EpisodeId = episode.Id,
                    State = dto.State,
                    IsManualOverride = true,
                    LastUpdated = DateTime.UtcNow,
                });
            }
            else
            {
                ws.State = dto.State;
                ws.IsManualOverride = true;
                ws.LastUpdated = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();

        if (dto.State == WatchState.Seen)
        {
            var watchEvents = allEpisodes.Select(ep => new WatchEvent
            {
                ProfileId = profileId,
                MediaItemId = series.MediaItemId,
                EpisodeId = ep.Id,
                EventType = WatchEventType.Finished,
                Source = SyncSource.Manual,
                Timestamp = dto.Timestamp?.ToUniversalTime() ?? DateTime.UtcNow
            });
            _context.WatchEvents.AddRange(watchEvents);
            await _context.SaveChangesAsync();
        }
        else if (dto.State == WatchState.Unseen)
        {
            var episodeIds = allEpisodes.Select(ep => ep.Id).ToList();
            var lastFinishedEvents = await _context.WatchEvents
                .Where(e => e.ProfileId == profileId && e.EpisodeId.HasValue && episodeIds.Contains(e.EpisodeId.Value) && e.EventType == WatchEventType.Finished)
                .GroupBy(e => e.EpisodeId)
                .Select(g => g.OrderByDescending(e => e.Timestamp).First())
                .ToListAsync();
            if (lastFinishedEvents.Count > 0)
            {
                _context.WatchEvents.RemoveRange(lastFinishedEvents);
                await _context.SaveChangesAsync();
            }
        }

        foreach (var episode in allEpisodes)
            await _propagationService.PropagateStateChangeAsync(profileId, series.MediaItemId, episode.Id, null, dto.State, dto.Timestamp?.ToUniversalTime());

        return Ok(new { message = $"All episodes for series {seriesId} set to {dto.State}" });
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

    // ── Remove from profile list (any authenticated user, own profile) ─────────

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
            _context.ProfileMediaBlocks.Add(new Domain.ProfileMediaBlock
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
