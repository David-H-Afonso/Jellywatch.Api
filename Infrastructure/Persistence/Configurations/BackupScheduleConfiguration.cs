using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class BackupScheduleConfiguration : IEntityTypeConfiguration<BackupSchedule>
{
    public void Configure(EntityTypeBuilder<BackupSchedule> e)
    {
        e.ToTable("backup_schedule");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.UserId).HasColumnName("user_id");
        e.Property(x => x.IsEnabled).HasColumnName("is_enabled").HasDefaultValue(false);
        e.Property(x => x.BackupHour).HasColumnName("backup_hour").HasDefaultValue(3);
        e.Property(x => x.BackupMinute).HasColumnName("backup_minute").HasDefaultValue(0);
        e.Property(x => x.DestinationPath).HasColumnName("destination_path").HasDefaultValue("/backups");
        e.Property(x => x.FileNamePrefix).HasColumnName("file_name_prefix").HasDefaultValue("");
        e.Property(x => x.FileNameSuffix).HasColumnName("file_name_suffix").HasDefaultValue("");
        e.Property(x => x.RetentionCount).HasColumnName("retention_count").HasDefaultValue(7);
        e.Property(x => x.LastRunAt).HasColumnName("last_run_at");
        e.Property(x => x.LastRunStatus).HasColumnName("last_run_status").HasDefaultValue("never");
        e.Property(x => x.LastRunMessage).HasColumnName("last_run_message");
        e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.UserId).IsUnique();
    }
}
