using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Application.Interfaces;

public interface IBackupScheduleManagementService
{
    Task<ServiceResult<BackupScheduleDto>> GetScheduleAsync(int userId);
    Task<ServiceResult<BackupScheduleDto>> UpdateScheduleAsync(int userId, UpdateBackupScheduleRequest req);
    ServiceResult<object> RunNow(int userId);
    Task<ServiceResult<List<UserBackupScheduleDto>>> GetAllUserSchedulesAsync(int currentUserId);
    Task<ServiceResult<UserBackupScheduleDto>> GetUserScheduleAsync(int currentUserId, int userId);
    Task<ServiceResult<UserBackupScheduleDto>> UpdateUserScheduleAsync(int currentUserId, int userId, UpdateBackupScheduleRequest req);
    Task<ServiceResult<object>> RunNowForUserAsync(int currentUserId, int userId);
}
