using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class SeriesConfiguration : IEntityTypeConfiguration<Series>
{
    public void Configure(EntityTypeBuilder<Series> e)
    {
        e.ToTable("series");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.MediaItemId).HasColumnName("media_item_id");
        e.Property(x => x.TotalSeasons).HasColumnName("total_seasons");
        e.Property(x => x.TotalEpisodes).HasColumnName("total_episodes");
        e.Property(x => x.Network).HasColumnName("network");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasOne(x => x.MediaItem).WithOne(mi => mi.Series).HasForeignKey<Series>(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
    }
}
