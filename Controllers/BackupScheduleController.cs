using System.Security.Claims;
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

    public record UserBackupScheduleDto(
        int UserId,
        string Username,
        BackupScheduleDto Schedule
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

    private static BackupScheduleDto DefaultDto() =>
        new(false, 3, 0, "/backups", "", "", 7, null, "never", null);

    private void RequireAdmin()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value;
        if (role != "Admin")
            throw new UnauthorizedAccessException("Admin role required");
    }

    // ── Admin endpoints ───────────────────────────────────────────────────────

    // GET /api/backupschedule/admin/users — list all users with their schedules
    [HttpGet("admin/users")]
    public async Task<IActionResult> GetAllUserSchedules()
    {
        RequireAdmin();

        var users = await _context.Users.OrderBy(u => u.Username).ToListAsync();
        var schedules = await _context.BackupSchedules.ToListAsync();
        var scheduleMap = schedules.ToDictionary(s => s.UserId);

        var result = users.Select(u => new UserBackupScheduleDto(
            u.Id,
            u.Username,
            scheduleMap.TryGetValue(u.Id, out var s) ? ToDto(s) : DefaultDto()
        )).ToList();

        return Ok(result);
    }

    // GET /api/backupschedule/admin/{userId} — get a specific user's schedule
    [HttpGet("admin/{userId:int}")]
    public async Task<IActionResult> GetUserSchedule(int userId)
    {
        RequireAdmin();

        var user = await _context.Users.FindAsync(userId);
        if (user == null) return NotFound(new { message = "User not found" });

        var schedule = await _context.BackupSchedules
            .FirstOrDefaultAsync(s => s.UserId == userId);

        return Ok(new UserBackupScheduleDto(
            user.Id,
            user.Username,
            schedule != null ? ToDto(schedule) : DefaultDto()
        ));
    }

    // PUT /api/backupschedule/admin/{userId} — update a specific user's schedule
    [HttpPut("admin/{userId:int}")]
    public async Task<IActionResult> UpdateUserSchedule(int userId, [FromBody] UpdateBackupScheduleRequest req)
    {
        RequireAdmin();

        var user = await _context.Users.FindAsync(userId);
        if (user == null) return NotFound(new { message = "User not found" });

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
        return Ok(new UserBackupScheduleDto(user.Id, user.Username, ToDto(schedule)));
    }

    // POST /api/backupschedule/admin/{userId}/run-now — trigger backup for a specific user
    [HttpPost("admin/{userId:int}/run-now")]
    public IActionResult RunNowForUser(int userId)
    {
        RequireAdmin();

        _ = Task.Run(() => _backupService.RunBackupForUserAsync(userId));
        return Accepted(new { message = "Backup started in the background." });
    }
}
