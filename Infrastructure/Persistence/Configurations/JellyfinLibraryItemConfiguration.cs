using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class JellyfinLibraryItemConfiguration : IEntityTypeConfiguration<JellyfinLibraryItem>
{
    public void Configure(EntityTypeBuilder<JellyfinLibraryItem> e)
    {
        e.ToTable("jellyfin_library_item");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.JellyfinItemId).HasColumnName("jellyfin_item_id");
        e.Property(x => x.JellyfinParentId).HasColumnName("jellyfin_parent_id");
        e.Property(x => x.Name).HasColumnName("name");
        e.Property(x => x.Type).HasColumnName("type");
        e.Property(x => x.MediaItemId).HasColumnName("media_item_id");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasOne(x => x.MediaItem).WithMany().HasForeignKey(x => x.MediaItemId).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => x.JellyfinItemId).IsUnique();
    }
}
