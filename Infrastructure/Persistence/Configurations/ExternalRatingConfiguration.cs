using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class ExternalRatingConfiguration : IEntityTypeConfiguration<ExternalRating>
{
    public void Configure(EntityTypeBuilder<ExternalRating> e)
    {
        e.ToTable("external_rating");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.MediaItemId).HasColumnName("media_item_id");
        e.Property(x => x.Provider).HasColumnName("provider");
        e.Property(x => x.Score).HasColumnName("score");
        e.Property(x => x.VoteCount).HasColumnName("vote_count");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasOne(x => x.MediaItem).WithMany(mi => mi.ExternalRatings).HasForeignKey(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasIndex(x => new { x.MediaItemId, x.Provider }).IsUnique();
    }
}
