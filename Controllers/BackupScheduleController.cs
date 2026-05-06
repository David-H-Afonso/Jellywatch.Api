using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Infrastructure;
using Jellywatch.Api.Services;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class BackupScheduleController : BaseApiController
{
    private readonly JellywatchDbContext _context;
    private readonly BackupScheduleService _backupService;

    public BackupScheduleController(JellywatchDbContext context, BackupScheduleService backupService)
    {
        _context = context;
        _backupService = backupService;
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    public record BackupScheduleDto(
        bool IsEnabled,
        int BackupHour,
        int BackupMinute,
        string DestinationPath,
        string FileNamePrefix,
        string FileNameSuffix,
        int RetentionCount,
        DateTime? LastRunAt,
        string LastRunStatus,
        string? LastRunMessage
    );

    public record UpdateBackupScheduleRequest(
        bool IsEnabled,
        int BackupHour,
        int BackupMinute,
        string DestinationPath,
        string FileNamePrefix,
        string FileNameSuffix,
        int RetentionCount
    );

    // ── GET /api/backupschedule ───────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> GetSchedule()
    {
        var authResult = RequireUserId();
        if (authResult is not OkResult) return authResult;
        var userId = CurrentUserId!.Value;

        var schedule = await _context.BackupSchedules
            .FirstOrDefaultAsync(s => s.UserId == userId);

        if (schedule == null)
            return Ok(new BackupScheduleDto(false, 3, 0, "/backups", "", "", 7, null, "never", null));

        return Ok(ToDto(schedule));
    }

    // ── PUT /api/backupschedule ───────────────────────────────────────────────

    [HttpPut]
    public async Task<IActionResult> UpdateSchedule([FromBody] UpdateBackupScheduleRequest req)
    {
        var authResult = RequireUserId();
        if (authResult is not OkResult) return authResult;
        var userId = CurrentUserId!.Value;

        var schedule = await _context.BackupSchedules
            .FirstOrDefaultAsync(s => s.UserId == userId);

        if (schedule == null)
        {
            schedule = new BackupSchedule { UserId = userId };
            _context.BackupSchedules.Add(schedule);
        }

        schedule.IsEnabled = req.IsEnabled;
        schedule.BackupHour = req.BackupHour;
        schedule.BackupMinute = req.BackupMinute;
        schedule.DestinationPath = req.DestinationPath;
        schedule.FileNamePrefix = req.FileNamePrefix ?? "";
        schedule.FileNameSuffix = req.FileNameSuffix ?? "";
        schedule.RetentionCount = req.RetentionCount;

        await _context.SaveChangesAsync();
        return Ok(ToDto(schedule));
    }

    // ── POST /api/backupschedule/run-now ─────────────────────────────────────

    [HttpPost("run-now")]
    public async Task<IActionResult> RunNow()
    {
        var authResult = RequireUserId();
        if (authResult is not OkResult) return authResult;
        var userId = CurrentUserId!.Value;

        _ = Task.Run(() => _backupService.RunBackupForUserAsync(userId));
        return Accepted(new { message = "Backup started in the background." });
    }

    // ── Helper ────────────────────────────────────────────────────────────────

    private static BackupScheduleDto ToDto(BackupSchedule s) => new(
        s.IsEnabled, s.BackupHour, s.BackupMinute,
        s.DestinationPath, s.FileNamePrefix, s.FileNameSuffix,
        s.RetentionCount, s.LastRunAt, s.LastRunStatus, s.LastRunMessage);
}
