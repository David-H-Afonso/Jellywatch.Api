using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class WatchlistConfiguration : IEntityTypeConfiguration<Watchlist>
{
    public void Configure(EntityTypeBuilder<Watchlist> e)
    {
        e.ToTable("watchlist");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.Name).HasColumnName("name").HasMaxLength(160);
        e.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
        e.Property(x => x.CoverImagePath).HasColumnName("cover_image_path");
        e.Property(x => x.JellyfinPlaylistId).HasColumnName("jellyfin_playlist_id");
        e.Property(x => x.OwnerUserId).HasColumnName("owner_user_id");
        e.Property(x => x.State).HasColumnName("state");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasOne(x => x.OwnerUser).WithMany(u => u.OwnedWatchlists).HasForeignKey(x => x.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => x.OwnerUserId);
    }
}
