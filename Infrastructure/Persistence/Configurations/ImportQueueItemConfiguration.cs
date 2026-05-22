using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class ImportQueueItemConfiguration : IEntityTypeConfiguration<ImportQueueItem>
{
    public void Configure(EntityTypeBuilder<ImportQueueItem> e)
    {
        e.ToTable("import_queue_item");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.JellyfinItemId).HasColumnName("jellyfin_item_id");
        e.Property(x => x.MediaType).HasColumnName("media_type");
        e.Property(x => x.Priority).HasColumnName("priority");
        e.Property(x => x.Status).HasColumnName("status");
        e.Property(x => x.RetryCount).HasColumnName("retry_count");
        e.Property(x => x.NextRetryAt).HasColumnName("next_retry_at");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.HasIndex(x => x.JellyfinItemId);
        e.HasIndex(x => x.Status);
    }
}
