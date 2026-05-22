using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure.Persistence;
using Jellywatch.Api.Application.Services;
using Jellywatch.Api.Infrastructure.BackgroundJobs;

namespace Jellywatch.Api.Application.Services;

public class WatchStateService : IWatchStateService
{
    private readonly JellywatchDbContext _context;
    private readonly IPropagationService _propagationService;

    public WatchStateService(JellywatchDbContext context, IPropagationService propagationService)
    {
        _context = context;
        _propagationService = propagationService;
    }

    public async Task<(bool Success, string Message, string? Error)> UpdateEpisodeStateAsync(int profileId, int episodeId, WatchStateUpdateDto dto)
    {
        var episode = await _context.Episodes
            .Include(e => e.Season)
            .FirstOrDefaultAsync(e => e.Id == episodeId);

        if (episode is null)
            return (false, "", "Episode not found");

        var series = await _context.Series.FirstOrDefaultAsync(s => s.Id == episode.Season.SeriesId);
        if (series is null)
            return (false, "", "Series not found");

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
            var manualEvents = await _context.WatchEvents
                .Where(e => e.ProfileId == profileId
                    && e.EpisodeId == episodeId
                    && e.EventType == WatchEventType.Finished
                    && e.Source == SyncSource.Manual)
                .ToListAsync();
            if (manualEvents.Count > 0)
            {
                _context.WatchEvents.RemoveRange(manualEvents);
                await _context.SaveChangesAsync();
            }
        }

        await _propagationService.PropagateStateChangeAsync(profileId, series.MediaItemId, episodeId, null, dto.State, dto.Timestamp?.ToUniversalTime());

        return (true, $"Episode state updated", null);
    }

    public async Task<(bool Success, string Message, string? Error)> UpdateMovieStateAsync(int profileId, int movieId, WatchStateUpdateDto dto)
    {
        var movie = await _context.Movies.FindAsync(movieId);
        if (movie is null)
            return (false, "", "Movie not found");

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

        return (true, $"Movie state updated", null);
    }

    public async Task<(bool Success, string Message, string? Error)> UpdateSeasonStateAsync(int profileId, int seasonId, WatchStateUpdateDto dto)
    {
        var season = await _context.Seasons
            .Include(s => s.Episodes)
            .Include(s => s.Series)
            .FirstOrDefaultAsync(s => s.Id == seasonId);

        if (season == null)
            return (false, "", "Season not found");

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
            var manualEvents = await _context.WatchEvents
                .Where(e => e.ProfileId == profileId
                    && e.EpisodeId.HasValue
                    && episodeIds.Contains(e.EpisodeId.Value)
                    && e.EventType == WatchEventType.Finished
                    && e.Source == SyncSource.Manual)
                .ToListAsync();
            if (manualEvents.Count > 0)
            {
                _context.WatchEvents.RemoveRange(manualEvents);
                await _context.SaveChangesAsync();
            }
        }

        foreach (var episode in season.Episodes)
            await _propagationService.PropagateStateChangeAsync(profileId, season.Series.MediaItemId, episode.Id, null, dto.State, dto.Timestamp?.ToUniversalTime());

        return (true, $"Season {seasonId} state updated to {dto.State}", null);
    }

    public async Task<(bool Success, string Message, string? Error)> UpdateSeriesStateAsync(int profileId, int seriesId, WatchStateUpdateDto dto)
    {
        var series = await _context.Series
            .Include(s => s.Seasons)
                .ThenInclude(se => se.Episodes)
            .FirstOrDefaultAsync(s => s.Id == seriesId);

        if (series == null)
            return (false, "", "Series not found");

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
            var manualEvents = await _context.WatchEvents
                .Where(e => e.ProfileId == profileId
                    && e.EpisodeId.HasValue
                    && episodeIds.Contains(e.EpisodeId.Value)
                    && e.EventType == WatchEventType.Finished
                    && e.Source == SyncSource.Manual)
                .ToListAsync();
            if (manualEvents.Count > 0)
            {
                _context.WatchEvents.RemoveRange(manualEvents);
                await _context.SaveChangesAsync();
            }
        }

        foreach (var episode in allEpisodes)
            await _propagationService.PropagateStateChangeAsync(profileId, series.MediaItemId, episode.Id, null, dto.State, dto.Timestamp?.ToUniversalTime());

        return (true, $"All episodes for series {seriesId} set to {dto.State}", null);
    }
}