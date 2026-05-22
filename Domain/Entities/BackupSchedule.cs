namespace Jellywatch.Api.Domain.Entities;

public class BackupSchedule
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public bool IsEnabled { get; set; } = false;
    public int BackupHour { get; set; } = 3;
    public int BackupMinute { get; set; } = 0;
    public string DestinationPath { get; set; } = "/backups";
    public string FileNamePrefix { get; set; } = "";
    public string FileNameSuffix { get; set; } = "";
    public int RetentionCount { get; set; } = 7;
    public DateTime? LastRunAt { get; set; }
    public string LastRunStatus { get; set; } = "never";
    public string? LastRunMessage { get; set; }

    public virtual User User { get; set; } = null!;
}
