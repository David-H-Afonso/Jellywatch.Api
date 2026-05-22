using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class SyncJobConfiguration : IEntityTypeConfiguration<SyncJob>
{
    public void Configure(EntityTypeBuilder<SyncJob> e)
    {
        e.ToTable("sync_job");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.Type).HasColumnName("type");
        e.Property(x => x.Status).HasColumnName("status");
        e.Property(x => x.ProfileId).HasColumnName("profile_id");
        e.Property(x => x.StartedAt).HasColumnName("started_at");
        e.Property(x => x.CompletedAt).HasColumnName("completed_at");
        e.Property(x => x.ItemsProcessed).HasColumnName("items_processed");
        e.Property(x => x.ErrorMessage).HasColumnName("error_message");
        e.HasOne(x => x.Profile).WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.SetNull);
    }
}
