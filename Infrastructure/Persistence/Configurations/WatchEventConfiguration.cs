using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class WatchEventConfiguration : IEntityTypeConfiguration<WatchEvent>
{
    public void Configure(EntityTypeBuilder<WatchEvent> e)
    {
        e.ToTable("watch_event");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.ProfileId).HasColumnName("profile_id");
        e.Property(x => x.MediaItemId).HasColumnName("media_item_id");
        e.Property(x => x.EpisodeId).HasColumnName("episode_id");
        e.Property(x => x.MovieId).HasColumnName("movie_id");
        e.Property(x => x.JellyfinItemId).HasColumnName("jellyfin_item_id");
        e.Property(x => x.EventType).HasColumnName("event_type");
        e.Property(x => x.PositionTicks).HasColumnName("position_ticks");
        e.Property(x => x.Source).HasColumnName("source");
        e.Property(x => x.Timestamp).HasColumnName("timestamp");
        e.HasOne(x => x.Profile).WithMany(p => p.WatchEvents).HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.MediaItem).WithMany().HasForeignKey(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Episode).WithMany(ep => ep.WatchEvents).HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne(x => x.Movie).WithMany(mv => mv.WatchEvents).HasForeignKey(x => x.MovieId).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => x.JellyfinItemId);
        e.HasIndex(x => x.Timestamp);
    }
}
