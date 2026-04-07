using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Helpers;
using Jellywatch.Api.Infrastructure;
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

    public SyncController(JellywatchDbContext context, ISyncOrchestrationService syncService, ILogger<SyncController> logger, IPropagationService propagationService)
    {
        _context = context;
        _syncService = syncService;
        _logger = logger;
        _propagationService = propagationService;
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
}
