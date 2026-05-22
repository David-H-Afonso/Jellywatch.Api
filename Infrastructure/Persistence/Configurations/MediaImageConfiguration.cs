using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class MediaImageConfiguration : IEntityTypeConfiguration<MediaImage>
{
    public void Configure(EntityTypeBuilder<MediaImage> e)
    {
        e.ToTable("media_image");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.MediaItemId).HasColumnName("media_item_id");
        e.Property(x => x.SeasonId).HasColumnName("season_id");
        e.Property(x => x.EpisodeId).HasColumnName("episode_id");
        e.Property(x => x.ImageType).HasColumnName("image_type");
        e.Property(x => x.RemoteUrl).HasColumnName("remote_url");
        e.Property(x => x.LocalPath).HasColumnName("local_path");
        e.Property(x => x.Width).HasColumnName("width");
        e.Property(x => x.Height).HasColumnName("height");
        e.Property(x => x.Language).HasColumnName("language");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.HasOne(x => x.MediaItem).WithMany(mi => mi.Images).HasForeignKey(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
        e.HasOne(x => x.Season).WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.SetNull);
        e.HasOne(x => x.Episode).WithMany().HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.SetNull);
    }
}
