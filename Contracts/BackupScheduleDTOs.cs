namespace Jellywatch.Api.Contracts;

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
