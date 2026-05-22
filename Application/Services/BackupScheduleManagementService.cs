using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Infrastructure.Persistence;

namespace Jellywatch.Api.Application.Services;

public class BackupScheduleManagementService : IBackupScheduleManagementService
{
    private readonly JellywatchDbContext _context;
    private readonly Jellywatch.Api.Infrastructure.BackgroundJobs.BackupScheduleService _backupService;

    public BackupScheduleManagementService(JellywatchDbContext context, Jellywatch.Api.Infrastructure.BackgroundJobs.BackupScheduleService backupService)
    {
        _context = context;
        _backupService = backupService;
    }

    private async Task<bool> IsAdminAsync(int currentUserId) =>
        (await _context.Users.FindAsync(currentUserId))?.IsAdmin == true;

    private static BackupScheduleDto ToDto(BackupSchedule s) => new(
        s.IsEnabled, s.BackupHour, s.BackupMinute,
        s.DestinationPath, s.FileNamePrefix, s.FileNameSuffix,
        s.RetentionCount, s.LastRunAt, s.LastRunStatus, s.LastRunMessage);

    private static BackupScheduleDto DefaultDto() =>
        new(false, 3, 0, "/backups", "", "", 7, null, "never", null);

    // ── User endpoints ────────────────────────────────────────────────────────

    public async Task<ServiceResult<BackupScheduleDto>> GetScheduleAsync(int userId)
    {
        var schedule = await _context.BackupSchedules
            .FirstOrDefaultAsync(s => s.UserId == userId);

        if (schedule == null)
            return ServiceResult<BackupScheduleDto>.Ok(DefaultDto());

        return ServiceResult<BackupScheduleDto>.Ok(ToDto(schedule));
    }

    public async Task<ServiceResult<BackupScheduleDto>> UpdateScheduleAsync(int userId, UpdateBackupScheduleRequest req)
    {
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
        return ServiceResult<BackupScheduleDto>.Ok(ToDto(schedule));
    }

    public ServiceResult<object> RunNow(int userId)
    {
        _ = Task.Run(() => _backupService.RunBackupForUserAsync(userId));
        return ServiceResult<object>.Ok(new { message = "Backup started in the background." });
    }

    // ── Admin endpoints ───────────────────────────────────────────────────────

    public async Task<ServiceResult<List<UserBackupScheduleDto>>> GetAllUserSchedulesAsync(int currentUserId)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<List<UserBackupScheduleDto>>.Fail("Admin role required", 403);

        var users = await _context.Users.OrderBy(u => u.Username).ToListAsync();
        var schedules = await _context.BackupSchedules.ToListAsync();
        var scheduleMap = schedules.ToDictionary(s => s.UserId);

        var result = users.Select(u => new UserBackupScheduleDto(
            u.Id,
            u.Username,
            scheduleMap.TryGetValue(u.Id, out var s) ? ToDto(s) : DefaultDto()
        )).ToList();

        return ServiceResult<List<UserBackupScheduleDto>>.Ok(result);
    }

    public async Task<ServiceResult<UserBackupScheduleDto>> GetUserScheduleAsync(int currentUserId, int userId)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<UserBackupScheduleDto>.Fail("Admin role required", 403);

        var user = await _context.Users.FindAsync(userId);
        if (user == null) return ServiceResult<UserBackupScheduleDto>.Fail("User not found", 404);

        var schedule = await _context.BackupSchedules
            .FirstOrDefaultAsync(s => s.UserId == userId);

        return ServiceResult<UserBackupScheduleDto>.Ok(new UserBackupScheduleDto(
            user.Id,
            user.Username,
            schedule != null ? ToDto(schedule) : DefaultDto()
        ));
    }

    public async Task<ServiceResult<UserBackupScheduleDto>> UpdateUserScheduleAsync(int currentUserId, int userId, UpdateBackupScheduleRequest req)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<UserBackupScheduleDto>.Fail("Admin role required", 403);

        var user = await _context.Users.FindAsync(userId);
        if (user == null) return ServiceResult<UserBackupScheduleDto>.Fail("User not found", 404);

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
        return ServiceResult<UserBackupScheduleDto>.Ok(new UserBackupScheduleDto(user.Id, user.Username, ToDto(schedule)));
    }

    public async Task<ServiceResult<object>> RunNowForUserAsync(int currentUserId, int userId)
    {
        if (!await IsAdminAsync(currentUserId))
            return ServiceResult<object>.Fail("Admin role required", 403);

        _ = Task.Run(() => _backupService.RunBackupForUserAsync(userId));
        return ServiceResult<object>.Ok(new { message = "Backup started in the background." });
    }
}
