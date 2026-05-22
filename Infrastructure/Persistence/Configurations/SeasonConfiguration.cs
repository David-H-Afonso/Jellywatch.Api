using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class SeasonConfiguration : IEntityTypeConfiguration<Season>
{
    public void Configure(EntityTypeBuilder<Season> e)
    {
        e.ToTable("season");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.SeriesId).HasColumnName("series_id");
        e.Property(x => x.SeasonNumber).HasColumnName("season_number");
        e.Property(x => x.Name).HasColumnName("name");
        e.Property(x => x.Overview).HasColumnName("overview");
        e.Property(x => x.PosterPath).HasColumnName("poster_path");
        e.Property(x => x.TmdbId).HasColumnName("tmdb_id");
        e.Property(x => x.EpisodeCount).HasColumnName("episode_count");
        e.Property(x => x.AirDate).HasColumnName("air_date");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasOne(x => x.Series).WithMany(s => s.Seasons).HasForeignKey(x => x.SeriesId).OnDelete(DeleteBehavior.Cascade);
    }
}
