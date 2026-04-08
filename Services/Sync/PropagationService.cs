using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure;

namespace Jellywatch.Api.Services.Sync;

public class PropagationService : IPropagationService
{
    private readonly JellywatchDbContext _context;
    private readonly ILogger<PropagationService> _logger;

    public PropagationService(JellywatchDbContext context, ILogger<PropagationService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task PropagateStateChangeAsync(int sourceProfileId, int mediaItemId, int? episodeId, int? movieId, WatchState newState, DateTime? timestamp = null)
    {
        // Never propagate removal of watch state — only upgrades
        if (newState == WatchState.Unseen) return;

        var rules = await _context.PropagationRules
            .Where(r => r.SourceProfileId == sourceProfileId && r.IsActive)
            .ToListAsync();

        if (rules.Count == 0) return;

        // If no timestamp was provided, try to look up the source profile's latest Finished event
        if (timestamp == null && newState == WatchState.Seen)
        {
            var sourceEvent = await _context.WatchEvents
                .Where(e => e.ProfileId == sourceProfileId
                    && e.MediaItemId == mediaItemId
                    && e.EpisodeId == episodeId
                    && e.MovieId == movieId
                    && e.EventType == WatchEventType.Finished)
                .OrderByDescending(e => e.Timestamp)
                .FirstOrDefaultAsync();
            timestamp = sourceEvent?.Timestamp;
        }

        foreach (var rule in rules)
        {
            var targetState = await _context.ProfileWatchStates
                .FirstOrDefaultAsync(s =>
                    s.ProfileId == rule.TargetProfileId &&
                    s.MediaItemId == mediaItemId &&
                    s.EpisodeId == episodeId &&
                    s.MovieId == movieId);

            if (targetState?.IsManualOverride == true)
            {
                _logger.LogDebug("Skipping propagation to profile {TargetId} — manual override", rule.TargetProfileId);
                continue;
            }

            // State only upgrades: Unseen < InProgress < Seen
            if (targetState != null && targetState.State >= newState)
            {
                continue;
            }

            if (targetState == null)
            {
                _context.ProfileWatchStates.Add(new ProfileWatchState
                {
                    ProfileId = rule.TargetProfileId,
                    MediaItemId = mediaItemId,
                    EpisodeId = episodeId,
                    MovieId = movieId,
                    State = newState,
                    IsManualOverride = false,
                });
                _logger.LogInformation("Propagated {State} to profile {TargetId} for media {MediaId}", newState, rule.TargetProfileId, mediaItemId);
            }
            else
            {
                targetState.State = newState;
                _logger.LogInformation("Upgraded state to {State} for profile {TargetId} media {MediaId}", newState, rule.TargetProfileId, mediaItemId);
            }

            // Create a WatchEvent for the target profile with the original timestamp
            if (newState == WatchState.Seen && timestamp.HasValue)
            {
                var eventExists = await _context.WatchEvents.AnyAsync(e =>
                    e.ProfileId == rule.TargetProfileId
                    && e.MediaItemId == mediaItemId
                    && e.EpisodeId == episodeId
                    && e.MovieId == movieId
                    && e.EventType == WatchEventType.Finished);
                if (!eventExists)
                {
                    _context.WatchEvents.Add(new WatchEvent
                    {
                        ProfileId = rule.TargetProfileId,
                        MediaItemId = mediaItemId,
                        EpisodeId = episodeId,
                        MovieId = movieId,
                        EventType = WatchEventType.Finished,
                        Source = SyncSource.Manual,
                        Timestamp = timestamp.Value,
                    });
                }
            }
        }

        await _context.SaveChangesAsync();
    }
}
