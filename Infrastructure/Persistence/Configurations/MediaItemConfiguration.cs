using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Jellywatch.Api.Domain.Entities;

namespace Jellywatch.Api.Infrastructure.Persistence.Configurations;

public class MediaItemConfiguration : IEntityTypeConfiguration<MediaItem>
{
    public void Configure(EntityTypeBuilder<MediaItem> e)
    {
        e.ToTable("media_item");
        e.Property(x => x.Id).HasColumnName("id");
        e.Property(x => x.MediaType).HasColumnName("media_type");
        e.Property(x => x.Title).HasColumnName("title");
        e.Property(x => x.OriginalTitle).HasColumnName("original_title");
        e.Property(x => x.Overview).HasColumnName("overview");
        e.Property(x => x.TmdbId).HasColumnName("tmdb_id");
        e.Property(x => x.ImdbId).HasColumnName("imdb_id");
        e.Property(x => x.TvdbId).HasColumnName("tvdb_id");
        e.Property(x => x.TvMazeId).HasColumnName("tvmaze_id");
        e.Property(x => x.PosterPath).HasColumnName("poster_path");
        e.Property(x => x.BackdropPath).HasColumnName("backdrop_path");
        e.Property(x => x.ReleaseDate).HasColumnName("release_date");
        e.Property(x => x.Status).HasColumnName("status");
        e.Property(x => x.OriginalLanguage).HasColumnName("original_language");
        e.Property(x => x.Genres).HasColumnName("genres");
        e.Property(x => x.CreatedAt).HasColumnName("created_at");
        e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        e.HasIndex(x => x.TmdbId);
        e.HasIndex(x => x.ImdbId);
        e.HasIndex(x => x.TvdbId);
        e.HasIndex(x => x.TvMazeId);
    }
}
