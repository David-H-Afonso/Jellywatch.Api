using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure;

namespace Jellywatch.Api.Services.Sync;

public class StateCalculationService : IStateCalculationService
{
    private readonly JellywatchDbContext _context;

    public StateCalculationService(JellywatchDbContext context)
    {
        _context = context;
    }

    public WatchState CalculateEpisodeState(bool played, long positionTicks, long? totalTicks)
    {
        if (played) return WatchState.Seen;
        if (positionTicks > 0) return WatchState.InProgress;
        return WatchState.Unseen;
    }

    public WatchState CalculateMovieState(bool played, long positionTicks, long? totalTicks)
    {
        if (played) return WatchState.Seen;
        if (positionTicks > 0) return WatchState.InProgress;
        return WatchState.Unseen;
    }

    public WatchState CalculateSeriesState(IEnumerable<WatchState> episodeStates)
    {
        var states = episodeStates.ToList();
        if (states.Count == 0) return WatchState.Unseen;
        if (states.All(s => s == WatchState.Seen)) return WatchState.Seen;
        if (states.Any(s => s == WatchState.InProgress || s == WatchState.Seen)) return WatchState.InProgress;
        return WatchState.Unseen;
    }

    public WatchState CalculateSeasonState(IEnumerable<WatchState> episodeStates)
    {
        return CalculateSeriesState(episodeStates);
    }

    public async Task RecalculateProfileWatchStateAsync(int profileId, int mediaItemId, int? episodeId = null, int? movieId = null)
    {
        var existing = await _context.ProfileWatchStates
            .FirstOrDefaultAsync(s =>
                s.ProfileId == profileId &&
                s.MediaItemId == mediaItemId &&
                s.EpisodeId == episodeId &&
                s.MovieId == movieId);

        if (existing?.IsManualOverride == true) return;

        // For episodes/movies: derive from latest watch events
        WatchState newState;
        if (episodeId.HasValue)
        {
            var events = await _context.WatchEvents
                .Where(e => e.ProfileId == profileId && e.EpisodeId == episodeId)
                .OrderByDescending(e => e.Timestamp)
                .ToListAsync();

            newState = DeriveStateFromEvents(events);
        }
        else if (movieId.HasValue)
        {
            var events = await _context.WatchEvents
                .Where(e => e.ProfileId == profileId && e.MovieId == movieId)
                .OrderByDescending(e => e.Timestamp)
                .ToListAsync();

            newState = DeriveStateFromEvents(events);
        }
        else
        {
            // Series-level: aggregate from episode states
            var mediaItem = await _context.MediaItems
                .Include(m => m.Series)
                    .ThenInclude(s => s!.Seasons)
                        .ThenInclude(s => s.Episodes)
                .FirstOrDefaultAsync(m => m.Id == mediaItemId);

            if (mediaItem?.Series == null) return;

            var episodeIds = mediaItem.Series.Seasons
                .SelectMany(s => s.Episodes)
                .Select(e => e.Id)
                .ToList();

            var episodeStates = await _context.ProfileWatchStates
                .Where(s => s.ProfileId == profileId && s.EpisodeId.HasValue && episodeIds.Contains(s.EpisodeId.Value))
                .Select(s => s.State)
                .ToListAsync();

            newState = CalculateSeriesState(episodeStates);
        }

        if (existing == null)
        {
            _context.ProfileWatchStates.Add(new ProfileWatchState
            {
                ProfileId = profileId,
                MediaItemId = mediaItemId,
                EpisodeId = episodeId,
                MovieId = movieId,
                State = newState,
                IsManualOverride = false,
            });
        }
        else if (existing.State != newState)
        {
            existing.State = newState;
        }

        await _context.SaveChangesAsync();
    }

    private static WatchState DeriveStateFromEvents(List<WatchEvent> events)
    {
        if (events.Count == 0) return WatchState.Unseen;

        var latest = events[0];
        if (latest.EventType == WatchEventType.Finished) return WatchState.Seen;
        if (latest.EventType == WatchEventType.Progress || latest.EventType == WatchEventType.Started) return WatchState.InProgress;
        return WatchState.Unseen;
    }
}
