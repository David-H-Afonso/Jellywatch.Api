using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class MetadataRefreshJobConfiguration : IEntityTypeConfiguration<MetadataRefreshJob>
{
    public void Configure(EntityTypeBuilder<MetadataRefreshJob> e)
    {
        e.ToTable("metadata_refresh_job");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.MediaItemId).HasColumnName("media_item_id");
        e.Property(x => x.Provider).HasColumnName("provider");
        e.Property(x => x.Status).HasColumnName("status");
        e.Property(x => x.LastRefreshed).HasColumnName("last_refreshed");
        e.Property(x => x.NextRefresh).HasColumnName("next_refresh");
        e.HasOne(x => x.MediaItem).WithMany().HasForeignKey(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
    }
}
