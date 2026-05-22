using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Common;
using Jellywatch.Api.Infrastructure.Persistence;
using Jellywatch.Api.Infrastructure.ExternalServices;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Application.Services;
using Jellywatch.Api.Infrastructure.BackgroundJobs;
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

    [HttpPost("force-propagate-series/{seriesId:int}")]
    public async Task<IActionResult> ForcePropagateSeriesFromParents(int seriesId, [FromQuery] int targetProfileId)
    {
        if (!CurrentUserId.HasValue) return Unauthorized();

        var currentUser = await _context.Users.FindAsync(CurrentUserId.Value);
        var targetProfile = await _context.Profiles.FindAsync(targetProfileId);
        if (targetProfile is null) return NotFound(new { message = "Target profile not found." });
        if (currentUser?.IsAdmin != true && targetProfile.UserId != CurrentUserId.Value) return Forbid();

        var series = await _context.Series
            .Include(s => s.Seasons)
                .ThenInclude(sea => sea.Episodes)
            .FirstOrDefaultAsync(s => s.Id == seriesId);
        if (series is null) return NotFound(new { message = "Series not found." });

        var sourceProfileIds = await _context.PropagationRules
            .Where(r => r.IsActive && r.TargetProfileId == targetProfileId)
            .Select(r => r.SourceProfileId)
            .Distinct()
            .ToListAsync();

        if (sourceProfileIds.Count == 0)
            return BadRequest(new { message = "This profile has no active parent propagation rules." });

        var updated = 0;
        foreach (var sourceProfileId in sourceProfileIds)
            updated += await ForcePropagateSeriesAsync(sourceProfileId, targetProfileId, series);

        await _context.SaveChangesAsync();
        return Ok(new
        {
            message = $"Forced parent propagation updated {updated} episode states.",
            updated,
            sourceProfileCount = sourceProfileIds.Count
        });
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

        var allEpisodes = series.Seasons.SelectMany(s => s.Episodes).ToList();
        var episodeIds = allEpisodes.Select(e => e.Id).ToList();
        var localBySeasonEpisode = allEpisodes
            .GroupBy(e => (e.Season.SeasonNumber, e.EpisodeNumber))
            .ToDictionary(g => g.Key, g => g.First());
        var localEpisodesOrdered = allEpisodes
            .Where(e => e.Season.SeasonNumber > 0)
            .OrderBy(e => e.Season.SeasonNumber)
            .ThenBy(e => e.EpisodeNumber)
            .ToList();
        if (localEpisodesOrdered.Count == 0)
        {
            localEpisodesOrdered = allEpisodes
                .OrderBy(e => e.Season.SeasonNumber)
                .ThenBy(e => e.EpisodeNumber)
                .ToList();
        }

        var jellyfinEpisodesOrdered = jellyfinEpisodes
            .Where(e => e.ParentIndexNumber.HasValue && e.IndexNumber.HasValue && e.ParentIndexNumber.Value > 0)
            .OrderBy(e => e.ParentIndexNumber!.Value)
            .ThenBy(e => e.IndexNumber!.Value)
            .ToList();
        if (jellyfinEpisodesOrdered.Count == 0)
        {
            jellyfinEpisodesOrdered = jellyfinEpisodes
                .Where(e => e.ParentIndexNumber.HasValue && e.IndexNumber.HasValue)
                .OrderBy(e => e.ParentIndexNumber!.Value)
                .ThenBy(e => e.IndexNumber!.Value)
                .ToList();
        }
        var jellyfinOrdinalById = jellyfinEpisodesOrdered
            .Select((episode, index) => new { episode.Id, index })
            .ToDictionary(x => x.Id, x => x.index);

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

        foreach (var jellyfinEp in jellyfinEpisodesOrdered)
        {
            if (jellyfinEp.UserData?.Played != true) continue;

            Episode? episode = null;
            if (jellyfinEp.ParentIndexNumber.HasValue && jellyfinEp.IndexNumber.HasValue)
            {
                localBySeasonEpisode.TryGetValue((jellyfinEp.ParentIndexNumber.Value, jellyfinEp.IndexNumber.Value), out episode);
            }

            if (episode is null
                && jellyfinOrdinalById.TryGetValue(jellyfinEp.Id, out var ordinal)
                && ordinal >= 0
                && ordinal < localEpisodesOrdered.Count)
            {
                episode = localEpisodesOrdered[ordinal];
                _logger.LogInformation(
                    "Matched Jellyfin episode {JellyfinSeason}x{JellyfinEpisode} to local {LocalSeason}x{LocalEpisode} by absolute order for series {SeriesId}",
                    jellyfinEp.ParentIndexNumber,
                    jellyfinEp.IndexNumber,
                    episode.Season.SeasonNumber,
                    episode.EpisodeNumber,
                    seriesId);
            }

            if (episode is null) continue;

            var activityLogTime = stopTimes.TryGetValue(jellyfinEp.Id, out var aDate) ? aDate : (DateTime?)null;
            var bestTimestamp = activityLogTime
                ?? jellyfinEp.UserData?.LastPlayedDate
                ?? DateTime.UtcNow;

            // Upsert ProfileWatchState to Seen (Jellyfin is the source of truth here)
            if (existingWatchStates.TryGetValue(episode.Id, out var watchState))
            {
                if (watchState.State != WatchState.Seen || watchState.IsManualOverride)
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
                existingEvent.JellyfinItemId = jellyfinEp.Id;
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

        await _context.SaveChangesAsync();

        _logger.LogInformation("Refreshed watch dates for {Updated} episodes of series {SeriesId} (profile {ProfileId})", updated, seriesId, profileId);

        return Ok(new { message = $"Watch dates refreshed for {updated} episodes.", updated });
    }

    private async Task<int> ForcePropagateSeriesAsync(int sourceProfileId, int targetProfileId, Series series)
    {
        var episodes = series.Seasons
            .SelectMany(season => season.Episodes)
            .ToList();
        var episodeIds = episodes.Select(e => e.Id).ToList();

        var sourceStates = await _context.ProfileWatchStates
            .Where(ws => ws.ProfileId == sourceProfileId
                && ws.EpisodeId.HasValue
                && episodeIds.Contains(ws.EpisodeId.Value)
                && ws.State != WatchState.Unseen)
            .ToListAsync();

        if (sourceStates.Count == 0) return 0;

        var targetStates = await _context.ProfileWatchStates
            .Where(ws => ws.ProfileId == targetProfileId
                && ws.EpisodeId.HasValue
                && episodeIds.Contains(ws.EpisodeId.Value))
            .ToDictionaryAsync(ws => ws.EpisodeId!.Value, ws => ws);

        var sourceFinishedEventRows = await _context.WatchEvents
            .Where(e => e.ProfileId == sourceProfileId
                && e.EpisodeId.HasValue
                && episodeIds.Contains(e.EpisodeId.Value)
                && e.EventType == WatchEventType.Finished)
            .ToListAsync();

        var sourceEventMap = sourceFinishedEventRows
            .GroupBy(e => e.EpisodeId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Timestamp).First());
        var targetFinishedEventRows = await _context.WatchEvents
            .Where(e => e.ProfileId == targetProfileId
                && e.EpisodeId.HasValue
                && episodeIds.Contains(e.EpisodeId.Value)
                && e.EventType == WatchEventType.Finished)
            .ToListAsync();
        var targetFinishedEvents = targetFinishedEventRows
            .GroupBy(e => e.EpisodeId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Timestamp).First());

        var updated = 0;
        foreach (var sourceState in sourceStates)
        {
            var episodeId = sourceState.EpisodeId!.Value;
            if (!targetStates.TryGetValue(episodeId, out var targetState))
            {
                targetState = new ProfileWatchState
                {
                    ProfileId = targetProfileId,
                    MediaItemId = series.MediaItemId,
                    EpisodeId = episodeId,
                    State = sourceState.State,
                    IsManualOverride = false,
                    LastUpdated = DateTime.UtcNow
                };
                _context.ProfileWatchStates.Add(targetState);
                targetStates[episodeId] = targetState;
                updated++;
            }
            else if (targetState.State != sourceState.State || targetState.IsManualOverride)
            {
                targetState.State = sourceState.State;
                targetState.IsManualOverride = false;
                targetState.LastUpdated = DateTime.UtcNow;
                updated++;
            }

            if (sourceState.State == WatchState.Seen && sourceEventMap.TryGetValue(episodeId, out var sourceEvent))
            {
                if (targetFinishedEvents.TryGetValue(episodeId, out var targetEvent))
                {
                    if (targetEvent.Timestamp != sourceEvent.Timestamp)
                    {
                        targetEvent.Timestamp = sourceEvent.Timestamp;
                        targetEvent.Source = SyncSource.Manual;
                        targetEvent.JellyfinItemId = sourceEvent.JellyfinItemId;
                        updated++;
                    }
                }
                else
                {
                    _context.WatchEvents.Add(new WatchEvent
                    {
                        ProfileId = targetProfileId,
                        MediaItemId = series.MediaItemId,
                        EpisodeId = episodeId,
                        JellyfinItemId = sourceEvent.JellyfinItemId,
                        EventType = WatchEventType.Finished,
                        Source = SyncSource.Manual,
                        Timestamp = sourceEvent.Timestamp
                    });
                    updated++;
                }
            }
        }

        return updated;
    }
}
