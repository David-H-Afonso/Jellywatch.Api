using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class EpisodeConfiguration : IEntityTypeConfiguration<Episode>
{
    public void Configure(EntityTypeBuilder<Episode> e)
    {
        e.ToTable("episode");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.SeasonId).HasColumnName("season_id");
        e.Property(x => x.EpisodeNumber).HasColumnName("episode_number");
        e.Property(x => x.Name).HasColumnName("name");
        e.Property(x => x.Overview).HasColumnName("overview");
        e.Property(x => x.StillPath).HasColumnName("still_path");
        e.Property(x => x.TmdbId).HasColumnName("tmdb_id");
        e.Property(x => x.AirDate).HasColumnName("air_date");
        e.Property(x => x.AirTime).HasColumnName("air_time");
        e.Property(x => x.AirTimeUtc).HasColumnName("air_time_utc");
        e.Property(x => x.Runtime).HasColumnName("runtime");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasOne(x => x.Season).WithMany(s => s.Episodes).HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Cascade);
    }
}
