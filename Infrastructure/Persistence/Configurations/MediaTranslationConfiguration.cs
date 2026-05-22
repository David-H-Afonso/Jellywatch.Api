using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class MediaTranslationConfiguration : IEntityTypeConfiguration<MediaTranslation>
{
    public void Configure(EntityTypeBuilder<MediaTranslation> e)
    {
        e.ToTable("media_translation");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.MediaItemId).HasColumnName("media_item_id");
        e.Property(x => x.Language).HasColumnName("language");
        e.Property(x => x.Title).HasColumnName("title");
        e.Property(x => x.Overview).HasColumnName("overview");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.HasOne(x => x.MediaItem).WithMany(mi => mi.Translations).HasForeignKey(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.MediaItemId, x.Language }).IsUnique();
    }
}
