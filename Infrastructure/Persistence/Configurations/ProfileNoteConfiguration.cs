using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class ProfileNoteConfiguration : IEntityTypeConfiguration<ProfileNote>
{
    public void Configure(EntityTypeBuilder<ProfileNote> e)
    {
        e.ToTable("profile_note");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.ProfileId).HasColumnName("profile_id");
        e.Property(x => x.MediaItemId).HasColumnName("media_item_id");
        e.Property(x => x.SeasonId).HasColumnName("season_id");
        e.Property(x => x.EpisodeId).HasColumnName("episode_id");
        e.Property(x => x.Text).HasColumnName("text");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasOne(x => x.Profile).WithMany(p => p.Notes).HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.MediaItem).WithMany(mi => mi.Notes).HasForeignKey(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Season).WithMany(s => s.Notes).HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne(x => x.Episode).WithMany(ep => ep.Notes).HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.SetNull);
        e.HasIndex(x => new { x.ProfileId, x.MediaItemId, x.SeasonId, x.EpisodeId }).IsUnique();
    }
}
