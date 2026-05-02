using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Helpers;
using Jellywatch.Api.Infrastructure;
using Jellywatch.Api.Services.Jellyfin;
using Jellywatch.Api.Services.Sync;
using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class SyncController : BaseApiController
{
    private readonly JellywatchDbContext _context;
    private readonly ISyncOrchestrationService _syncService;
    private readonly ILogger<SyncController> _logger;
    private readonly IPropagationService _propagationService;
    private readonly IJellyfinApiClient _jellyfinClient;

    public SyncController(JellywatchDbContext context, ISyncOrchestrationService syncService, ILogger<SyncController> logger, IPropagationService propagationService, IJellyfinApiClient jellyfinClient)
    {
        _context = context;
        _syncService = syncService;
        _logger = logger;
        _propagationService = propagationService;
        _jellyfinClient = jellyfinClient;
    }

    [HttpPost("trigger")]
    public async Task<IActionResult> TriggerFullSync()
    {
        var user = await _context.Users.FindAsync(CurrentUserId);
        if (user?.IsAdmin != true) return Forbid();
        await _syncService.RunFullSyncAsync();
        return Ok(new { message = "Full sync started." });
    }

    /// <summary>Sync all profiles belonging to the current user.</summary>
    [HttpPost("trigger-mine")]
    public async Task<IActionResult> TriggerMySync()
    {
        if (!CurrentUserId.HasValue) return Unauthorized();

        var profileIds = await _context.Profiles
            .Where(p => p.UserId == CurrentUserId.Value)
            .Select(p => p.Id)
            .ToListAsync();

        foreach (var pid in profileIds)
            await _syncService.RunFullSyncAsync(pid);

        return Ok(new { message = $"Sync completed for {profileIds.Count} profile(s)." });
    }

    [HttpPost("trigger/{profileId:int}")]
    public async Task<IActionResult> TriggerProfileSync(int profileId)
    {
        await _syncService.RunFullSyncAsync(profileId);
        return Ok(new { message = $"Sync started for profile {profileId}." });
    }

    [HttpPost("reconcile/{profileId:int}")]
    public async Task<IActionResult> Reconcile(int profileId)
    {
        await _syncService.ReconcileProfileAsync(profileId);
        return Ok(new { message = $"Reconciliation completed for profile {profileId}." });
    }

    /// <summary>
    /// Re-runs propagation for all existing watch states from all source profiles.
    /// Use this after changing propagation rules or after the propagation bug fix
    /// to backfill any items that were missed.
    /// </summary>
    [HttpPost("re-propagate")]
    public async Task<IActionResult> RePropagate()
    {
        var user = await _context.Users.FindAsync(CurrentUserId);
        if (user?.IsAdmin != true) return Forbid();

        var activeRules = await _context.PropagationRules
            .Where(r => r.IsActive)
            .Select(r => r.SourceProfileId)
            .Distinct()
            .ToListAsync();

        if (activeRules.Count == 0)
            return Ok(new { message = "No active propagation rules found.", propagated = 0 });

        var watchStates = await _context.ProfileWatchStates
            .Where(ws => activeRules.Contains(ws.ProfileId) && ws.State != Jellywatch.Api.Domain.Enums.WatchState.Unseen)
            .ToListAsync();

        int count = 0;
        foreach (var ws in watchStates)
        {
            await _propagationService.PropagateStateChangeAsync(ws.ProfileId, ws.MediaItemId, ws.EpisodeId, ws.MovieId, ws.State);
            count++;
        }

        return Ok(new { message = $"Re-propagation complete.", propagated = count });
    }

    [HttpGet("jobs")]
    public async Task<ActionResult<PagedResult<SyncJobDto>>> GetSyncJobs([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var query = _context.SyncJobs.OrderByDescending(j => j.StartedAt);
        var totalCount = await query.CountAsync();

        var jobs = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(j => new SyncJobDto
            {
                Id = j.Id,
                Type = j.Type,
                Status = j.Status,
                ProfileId = j.ProfileId,
                StartedAt = j.StartedAt,
                CompletedAt = j.CompletedAt,
                ItemsProcessed = j.ItemsProcessed,
                ErrorMessage = j.ErrorMessage,
            })
            .ToListAsync();

        return Ok(new PagedResult<SyncJobDto>
        {
            Data = jobs,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpGet("webhook-logs")]
    public async Task<ActionResult<PagedResult<WebhookEventLogDto>>> GetWebhookLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var query = _context.WebhookEventLogs.OrderByDescending(l => l.ReceivedAt);
        var totalCount = await query.CountAsync();

        var logs = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new WebhookEventLogDto
            {
                Id = l.Id,
                EventType = l.EventType ?? string.Empty,
                ReceivedAt = l.ReceivedAt,
                ProcessedAt = l.ProcessedAt,
                Success = l.Success,
                ErrorMessage = l.ErrorMessage,
            })
            .ToListAsync();

        return Ok(new PagedResult<WebhookEventLogDto>
        {
            Data = logs,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// Admin-only: for a specific series, re-fetches watch timestamps from Jellyfin
    /// and overwrites the Finished WatchEvent timestamps with the most accurate date
    /// (activity log > LastPlayedDate). Useful for fixing stale dates from old test data.
    /// </summary>
    [HttpPost("refresh-watch-dates/{seriesId:int}")]
    public async Task<IActionResult> RefreshSeriesWatchDates(int seriesId, [FromQuery] int profileId)
    {
        var user = await _context.Users.FindAsync(CurrentUserId);
        if (user?.IsAdmin != true) return Forbid();

        var series = await _context.Series
            .Include(s => s.Seasons)
                .ThenInclude(sea => sea.Episodes)
            .FirstOrDefaultAsync(s => s.Id == seriesId);

        if (series == null)
            return NotFound(new { message = "Series not found." });

        var profile = await _context.Profiles.FindAsync(profileId);
        if (profile == null)
            return NotFound(new { message = "Profile not found." });

        // Resolve the Jellyfin series ID.
        // Primary: JellyfinLibraryItems table (created during metadata import).
        // Fallback: look at an existing WatchEvent for an episode of this series that has a
        //           JellyfinItemId stored, fetch that episode from Jellyfin to get its SeriesId.
        string? jellyfinSeriesId = null;

        var jellyfinLink = await _context.JellyfinLibraryItems
            .FirstOrDefaultAsync(j => j.MediaItemId == series.MediaItemId);

        if (jellyfinLink != null)
        {
            jellyfinSeriesId = jellyfinLink.JellyfinItemId;
        }
        else
        {
            var allEpisodeIds = series.Seasons.SelectMany(s => s.Episodes).Select(e => e.Id).ToList();
            var sampleEvent = await _context.WatchEvents
                .Where(e => e.ProfileId == profileId
                         && e.MediaItemId == series.MediaItemId
                         && e.EpisodeId.HasValue
                         && allEpisodeIds.Contains(e.EpisodeId!.Value)
                         && e.JellyfinItemId != null)
                .FirstOrDefaultAsync();

            if (sampleEvent?.JellyfinItemId != null)
            {
                try
                {
                    var jellyfinEp = await _jellyfinClient.GetItemAsync(sampleEvent.JellyfinItemId, profile.JellyfinUserId);
                    jellyfinSeriesId = jellyfinEp?.SeriesId;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch Jellyfin episode {ItemId} to resolve series ID", sampleEvent.JellyfinItemId);
                }
            }
        }

        if (string.IsNullOrWhiteSpace(jellyfinSeriesId))
            return BadRequest(new { message = "Could not determine Jellyfin series ID. Make sure the series has been synced at least once." });

        // Fetch all episodes from Jellyfin for this profile and series
        List<JellyfinItemInfo> jellyfinEpisodes;
        try
        {
            jellyfinEpisodes = await _jellyfinClient.GetItemsAsync(
                profile.JellyfinUserId,
                parentId: jellyfinSeriesId,
                itemTypes: "Episode");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch Jellyfin episodes for series {SeriesId}", seriesId);
            return StatusCode(500, new { message = "Failed to fetch Jellyfin episodes." });
        }

        // Fetch activity log for accurate stop timestamps (go back 10 years to cover all historical data)
        var stopTimes = new Dictionary<string, DateTime>();
        try
        {
            var activityLog = await _jellyfinClient.GetActivityLogAsync(
                minDate: DateTime.UtcNow.AddYears(-10), limit: 5000);
            stopTimes = activityLog
                .Where(a => a.Type == "VideoPlaybackStopped"
                         && a.ItemId != null
                         && a.UserId == profile.JellyfinUserId)
                .GroupBy(a => a.ItemId!)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(a => a.Date).First().Date);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch activity log for profile {ProfileId} — will use LastPlayedDate fallback", profileId);
        }

        // Build lookup: (seasonNumber, episodeNumber) -> JellyfinItemInfo
        var jellyfinEpLookup = jellyfinEpisodes
            .Where(e => e.ParentIndexNumber.HasValue && e.IndexNumber.HasValue)
            .GroupBy(e => (e.ParentIndexNumber!.Value, e.IndexNumber!.Value))
            .ToDictionary(g => g.Key, g => g.First());

        var allEpisodes = series.Seasons.SelectMany(s => s.Episodes).ToList();
        var episodeIds = allEpisodes.Select(e => e.Id).ToList();

        // Load existing Finished WatchEvents for this profile/series
        var existingEvents = await _context.WatchEvents
            .Where(e => e.ProfileId == profileId
                     && e.EpisodeId.HasValue
                     && episodeIds.Contains(e.EpisodeId!.Value)
                     && e.EventType == WatchEventType.Finished)
            .ToListAsync();
        var eventsByEpisodeId = existingEvents.ToDictionary(e => e.EpisodeId!.Value, e => e);

        // Load existing ProfileWatchStates for this profile/series (all states)
        var existingWatchStates = await _context.ProfileWatchStates
            .Where(ws => ws.ProfileId == profileId
                      && ws.EpisodeId.HasValue
                      && episodeIds.Contains(ws.EpisodeId!.Value))
            .ToDictionaryAsync(ws => ws.EpisodeId!.Value, ws => ws);

        int updated = 0;

        foreach (var season in series.Seasons)
        {
            foreach (var episode in season.Episodes)
            {
                if (!jellyfinEpLookup.TryGetValue((season.SeasonNumber, episode.EpisodeNumber), out var jellyfinEp))
                    continue;

                // Jellyfin is the source of truth — only process episodes Jellyfin marks as played
                if (jellyfinEp.UserData?.Played != true) continue;

                var activityLogTime = stopTimes.TryGetValue(jellyfinEp.Id, out var aDate) ? aDate : (DateTime?)null;
                var bestTimestamp = activityLogTime
                    ?? jellyfinEp.UserData?.LastPlayedDate
                    ?? DateTime.UtcNow;

                // Upsert ProfileWatchState to Seen (Jellyfin is the source of truth here)
                if (existingWatchStates.TryGetValue(episode.Id, out var watchState))
                {
                    if (watchState.State != WatchState.Seen)
                    {
                        watchState.State = WatchState.Seen;
                        watchState.IsManualOverride = false;
                        watchState.LastUpdated = DateTime.UtcNow;
                    }
                }
                else
                {
                    _context.ProfileWatchStates.Add(new ProfileWatchState
                    {
                        ProfileId = profileId,
                        MediaItemId = series.MediaItemId,
                        EpisodeId = episode.Id,
                        State = WatchState.Seen,
                        IsManualOverride = false,
                        LastUpdated = DateTime.UtcNow,
                    });
                }

                if (eventsByEpisodeId.TryGetValue(episode.Id, out var existingEvent))
                {
                    existingEvent.Timestamp = bestTimestamp;
                }
                else
                {
                    _context.WatchEvents.Add(new WatchEvent
                    {
                        ProfileId = profileId,
                        MediaItemId = series.MediaItemId,
                        EpisodeId = episode.Id,
                        JellyfinItemId = jellyfinEp.Id,
                        EventType = WatchEventType.Finished,
                        Source = SyncSource.Polling,
                        Timestamp = bestTimestamp,
                    });
                }

                updated++;
            }
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Refreshed watch dates for {Updated} episodes of series {SeriesId} (profile {ProfileId})", updated, seriesId, profileId);

        return Ok(new { message = $"Watch dates refreshed for {updated} episodes.", updated });
    }
}
