using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> e)
    {
        e.ToTable("movie");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.MediaItemId).HasColumnName("media_item_id");
        e.Property(x => x.Runtime).HasColumnName("runtime");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasOne(x => x.MediaItem).WithOne(mi => mi.Movie).HasForeignKey<Movie>(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
    }
}
