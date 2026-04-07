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
    public async Task<ActionResult<PagedResult<ActivityDto>>> GetActivity(int profileId, [FromQuery] QueryParameters query)
    {
        var baseQuery = _context.WatchEvents
            .Where(e => e.ProfileId == profileId)
            .Include(e => e.MediaItem)
            .Include(e => e.Episode)
                .ThenInclude(ep => ep!.Season)
            .OrderByDescending(e => e.Timestamp)
            .AsQueryable();

        var totalCount = await baseQuery.CountAsync();

        var events = await baseQuery
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync();

        var dtos = events.Select(e => new ActivityDto
        {
            Id = e.Id,
            MediaTitle = e.MediaItem.Title,
            EpisodeName = e.Episode?.Name,
            EpisodeNumber = e.Episode?.EpisodeNumber,
            SeasonNumber = e.Episode?.Season?.SeasonNumber,
            MediaType = e.MediaItem.MediaType,
            EventType = e.EventType,
            Source = e.Source,
            Timestamp = e.Timestamp,
            PosterPath = e.MediaItem.PosterPath
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

        if (dto.State == WatchState.Seen || dto.State == WatchState.Unseen)
        {
            _context.WatchEvents.Add(new WatchEvent
            {
                ProfileId = profileId,
                MediaItemId = series.MediaItemId,
                EpisodeId = episodeId,
                EventType = dto.State == WatchState.Seen ? WatchEventType.Finished : WatchEventType.Removed,
                Source = SyncSource.Manual,
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        // Propagate if this profile is a joint profile
        await _propagationService.PropagateStateChangeAsync(profileId, series.MediaItemId, episodeId, null, dto.State);

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

        if (dto.State == WatchState.Seen || dto.State == WatchState.Unseen)
        {
            _context.WatchEvents.Add(new WatchEvent
            {
                ProfileId = profileId,
                MediaItemId = movie.MediaItemId,
                MovieId = movieId,
                EventType = dto.State == WatchState.Seen ? WatchEventType.Finished : WatchEventType.Removed,
                Source = SyncSource.Manual,
                Timestamp = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
        }

        await _propagationService.PropagateStateChangeAsync(profileId, movie.MediaItemId, null, movieId, dto.State);

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

        if (dto.State == WatchState.Seen || dto.State == WatchState.Unseen)
        {
            var watchEvents = season.Episodes.Select(ep => new WatchEvent
            {
                ProfileId = profileId,
                MediaItemId = season.Series.MediaItemId,
                EpisodeId = ep.Id,
                EventType = dto.State == WatchState.Seen ? WatchEventType.Finished : WatchEventType.Removed,
                Source = SyncSource.Manual,
                Timestamp = DateTime.UtcNow
            });
            _context.WatchEvents.AddRange(watchEvents);
            await _context.SaveChangesAsync();
        }

        foreach (var episode in season.Episodes)
            await _propagationService.PropagateStateChangeAsync(profileId, season.Series.MediaItemId, episode.Id, null, dto.State);

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

        if (dto.State == WatchState.Seen || dto.State == WatchState.Unseen)
        {
            var watchEvents = allEpisodes.Select(ep => new WatchEvent
            {
                ProfileId = profileId,
                MediaItemId = series.MediaItemId,
                EpisodeId = ep.Id,
                EventType = dto.State == WatchState.Seen ? WatchEventType.Finished : WatchEventType.Removed,
                Source = SyncSource.Manual,
                Timestamp = DateTime.UtcNow
            });
            _context.WatchEvents.AddRange(watchEvents);
            await _context.SaveChangesAsync();
        }

        foreach (var episode in allEpisodes)
            await _propagationService.PropagateStateChangeAsync(profileId, series.MediaItemId, episode.Id, null, dto.State);

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
}
