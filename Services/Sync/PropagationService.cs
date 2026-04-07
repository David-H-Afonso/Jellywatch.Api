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

    public async Task PropagateStateChangeAsync(int sourceProfileId, int mediaItemId, int? episodeId, int? movieId, WatchState newState)
    {
        // Never propagate removal of watch state — only upgrades
        if (newState == WatchState.Unseen) return;

        var rules = await _context.PropagationRules
            .Where(r => r.SourceProfileId == sourceProfileId && r.IsActive)
            .ToListAsync();

        if (rules.Count == 0) return;

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
        }

        await _context.SaveChangesAsync();
    }
}
