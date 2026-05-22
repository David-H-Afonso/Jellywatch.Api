using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class ProfileWatchStateConfiguration : IEntityTypeConfiguration<ProfileWatchState>
{
    public void Configure(EntityTypeBuilder<ProfileWatchState> e)
    {
        e.ToTable("profile_watch_state");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.ProfileId).HasColumnName("profile_id");
        e.Property(x => x.MediaItemId).HasColumnName("media_item_id");
        e.Property(x => x.EpisodeId).HasColumnName("episode_id");
        e.Property(x => x.SeasonId).HasColumnName("season_id");
        e.Property(x => x.MovieId).HasColumnName("movie_id");
        e.Property(x => x.State).HasColumnName("state");
        e.Property(x => x.IsManualOverride).HasColumnName("is_manual_override");
        e.Property(x => x.IncludeInDashboard).HasColumnName("include_in_dashboard");
        e.Property(x => x.ExcludeFromDashboard).HasColumnName("exclude_from_dashboard");
        e.Property(x => x.UserRating).HasColumnName("user_rating").HasColumnType("decimal(4,2)");
        e.Property(x => x.LastUpdated).HasColumnName("last_updated");
        e.HasOne(x => x.Profile).WithMany(p => p.WatchStates).HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.MediaItem).WithMany(mi => mi.WatchStates).HasForeignKey(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Episode).WithMany(ep => ep.WatchStates).HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne(x => x.Season).WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne(x => x.Movie).WithMany(mv => mv.WatchStates).HasForeignKey(x => x.MovieId).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => new { x.ProfileId, x.MediaItemId, x.EpisodeId, x.SeasonId, x.MovieId }).IsUnique();
    }
}
