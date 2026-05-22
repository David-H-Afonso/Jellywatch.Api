using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class WatchlistItemConfiguration : IEntityTypeConfiguration<WatchlistItem>
{
    public void Configure(EntityTypeBuilder<WatchlistItem> e)
    {
        e.ToTable("watchlist_item");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.WatchlistId).HasColumnName("watchlist_id");
        e.Property(x => x.ItemType).HasColumnName("item_type");
        e.Property(x => x.MediaItemId).HasColumnName("media_item_id");
        e.Property(x => x.ChildWatchlistId).HasColumnName("child_watchlist_id");
        e.Property(x => x.Status).HasColumnName("status");
        e.Property(x => x.Position).HasColumnName("position");
        e.Property(x => x.AddedByUserId).HasColumnName("added_by_user_id");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasOne(x => x.Watchlist).WithMany(w => w.Items).HasForeignKey(x => x.WatchlistId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.MediaItem).WithMany().HasForeignKey(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.ChildWatchlist).WithMany().HasForeignKey(x => x.ChildWatchlistId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne(x => x.AddedByUser).WithMany().HasForeignKey(x => x.AddedByUserId).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => new { x.WatchlistId, x.MediaItemId }).IsUnique().HasFilter("media_item_id IS NOT NULL");
        e.HasIndex(x => new { x.WatchlistId, x.ChildWatchlistId }).IsUnique().HasFilter("child_watchlist_id IS NOT NULL");
        e.HasIndex(x => new { x.WatchlistId, x.Position });
    }
}
