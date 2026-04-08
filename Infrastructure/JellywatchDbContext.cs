using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Infrastructure;

public class JellywatchDbContext : DbContext
{
    public JellywatchDbContext(DbContextOptions<JellywatchDbContext> options) : base(options)
    {
        ChangeTracker.LazyLoadingEnabled = false;
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Profile> Profiles { get; set; }
    public DbSet<PropagationRule> PropagationRules { get; set; }
    public DbSet<MediaItem> MediaItems { get; set; }
    public DbSet<Series> Series { get; set; }
    public DbSet<Season> Seasons { get; set; }
    public DbSet<Episode> Episodes { get; set; }
    public DbSet<Movie> Movies { get; set; }
    public DbSet<ExternalRating> ExternalRatings { get; set; }
    public DbSet<WatchEvent> WatchEvents { get; set; }
    public DbSet<ProfileWatchState> ProfileWatchStates { get; set; }
    public DbSet<ProfileNote> ProfileNotes { get; set; }
    public DbSet<SyncJob> SyncJobs { get; set; }
    public DbSet<WebhookEventLog> WebhookEventLogs { get; set; }
    public DbSet<ImportQueueItem> ImportQueueItems { get; set; }
    public DbSet<MetadataRefreshJob> MetadataRefreshJobs { get; set; }
    public DbSet<MediaImage> MediaImages { get; set; }
    public DbSet<MediaTranslation> MediaTranslations { get; set; }
    public DbSet<ProviderCacheEntry> ProviderCacheEntries { get; set; }
    public DbSet<JellyfinLibraryItem> JellyfinLibraryItems { get; set; }
    public DbSet<BlacklistedItem> BlacklistedItems { get; set; }
    public DbSet<ProfileMediaBlock> ProfileMediaBlocks { get; set; }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity is User u) { u.CreatedAt = now; u.UpdatedAt = now; }
                else if (entry.Entity is Profile p) { p.CreatedAt = now; p.UpdatedAt = now; }
                else if (entry.Entity is PropagationRule pr) { pr.CreatedAt = now; }
                else if (entry.Entity is MediaItem mi) { mi.CreatedAt = now; mi.UpdatedAt = now; }
                else if (entry.Entity is Series s) { s.CreatedAt = now; s.UpdatedAt = now; }
                else if (entry.Entity is Season se) { se.CreatedAt = now; se.UpdatedAt = now; }
                else if (entry.Entity is Episode ep) { ep.CreatedAt = now; ep.UpdatedAt = now; }
                else if (entry.Entity is Movie mv) { mv.CreatedAt = now; mv.UpdatedAt = now; }
                else if (entry.Entity is ExternalRating er) { er.UpdatedAt = now; }
                else if (entry.Entity is ProfileNote pn) { pn.CreatedAt = now; pn.UpdatedAt = now; }
                else if (entry.Entity is MediaImage img) { img.CreatedAt = now; }
                else if (entry.Entity is MediaTranslation mt) { mt.CreatedAt = now; }
                else if (entry.Entity is ImportQueueItem iq) { iq.CreatedAt = now; }
                else if (entry.Entity is BlacklistedItem bl) { bl.CreatedAt = now; }
                else if (entry.Entity is ProfileMediaBlock pmb) { pmb.CreatedAt = now; }
                else if (entry.Entity is JellyfinLibraryItem jli) { jli.CreatedAt = now; jli.UpdatedAt = now; }
            }
            else if (entry.State == EntityState.Modified)
            {
                if (entry.Entity is User u) { u.UpdatedAt = now; }
                else if (entry.Entity is Profile p) { p.UpdatedAt = now; }
                else if (entry.Entity is MediaItem mi) { mi.UpdatedAt = now; }
                else if (entry.Entity is Series s) { s.UpdatedAt = now; }
                else if (entry.Entity is Season se) { se.UpdatedAt = now; }
                else if (entry.Entity is Episode ep) { ep.UpdatedAt = now; }
                else if (entry.Entity is Movie mv) { mv.UpdatedAt = now; }
                else if (entry.Entity is ExternalRating er) { er.UpdatedAt = now; }
                else if (entry.Entity is ProfileNote pn) { pn.UpdatedAt = now; }
                else if (entry.Entity is ProfileWatchState pws) { pws.LastUpdated = now; }
                else if (entry.Entity is JellyfinLibraryItem jli) { jli.UpdatedAt = now; }
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── User ──
        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("user");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.JellyfinUserId).HasColumnName("jellyfin_user_id");
            e.Property(x => x.Username).HasColumnName("username");
            e.Property(x => x.IsAdmin).HasColumnName("is_admin");
            e.Property(x => x.AvatarUrl).HasColumnName("avatar_url");
            e.Property(x => x.PreferredLanguage).HasColumnName("preferred_language");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.JellyfinUserId).IsUnique();
            e.HasIndex(x => x.Username).IsUnique();
        });

        // ── Profile ──
        modelBuilder.Entity<Profile>(e =>
        {
            e.ToTable("profile");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.UserId).HasColumnName("user_id");
            e.Property(x => x.JellyfinUserId).HasColumnName("jellyfin_user_id");
            e.Property(x => x.DisplayName).HasColumnName("display_name");
            e.Property(x => x.IsJoint).HasColumnName("is_joint");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne(x => x.User).WithMany(u => u.Profiles).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.JellyfinUserId).IsUnique();
        });

        // ── PropagationRule ──
        modelBuilder.Entity<PropagationRule>(e =>
        {
            e.ToTable("propagation_rule");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.SourceProfileId).HasColumnName("source_profile_id");
            e.Property(x => x.TargetProfileId).HasColumnName("target_profile_id");
            e.Property(x => x.IsActive).HasColumnName("is_active");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.SourceProfile).WithMany(p => p.SourceRules).HasForeignKey(x => x.SourceProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.TargetProfile).WithMany(p => p.TargetRules).HasForeignKey(x => x.TargetProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SourceProfileId, x.TargetProfileId }).IsUnique();
        });

        // ── MediaItem ──
        modelBuilder.Entity<MediaItem>(e =>
        {
            e.ToTable("media_item");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MediaType).HasColumnName("media_type");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.OriginalTitle).HasColumnName("original_title");
            e.Property(x => x.Overview).HasColumnName("overview");
            e.Property(x => x.TmdbId).HasColumnName("tmdb_id");
            e.Property(x => x.ImdbId).HasColumnName("imdb_id");
            e.Property(x => x.TvMazeId).HasColumnName("tvmaze_id");
            e.Property(x => x.PosterPath).HasColumnName("poster_path");
            e.Property(x => x.BackdropPath).HasColumnName("backdrop_path");
            e.Property(x => x.ReleaseDate).HasColumnName("release_date");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.OriginalLanguage).HasColumnName("original_language");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.TmdbId);
            e.HasIndex(x => x.ImdbId);
            e.HasIndex(x => x.TvMazeId);
        });

        // ── Series ──
        modelBuilder.Entity<Series>(e =>
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
        });

        // ── Season ──
        modelBuilder.Entity<Season>(e =>
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
        });

        // ── Episode ──
        modelBuilder.Entity<Episode>(e =>
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
            e.Property(x => x.Runtime).HasColumnName("runtime");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne(x => x.Season).WithMany(s => s.Episodes).HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── Movie ──
        modelBuilder.Entity<Movie>(e =>
        {
            e.ToTable("movie");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MediaItemId).HasColumnName("media_item_id");
            e.Property(x => x.Runtime).HasColumnName("runtime");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne(x => x.MediaItem).WithOne(mi => mi.Movie).HasForeignKey<Movie>(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── ExternalRating ──
        modelBuilder.Entity<ExternalRating>(e =>
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
        });

        // ── WatchEvent ──
        modelBuilder.Entity<WatchEvent>(e =>
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
        });

        // ── ProfileWatchState ──
        modelBuilder.Entity<ProfileWatchState>(e =>
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
            e.Property(x => x.UserRating).HasColumnName("user_rating").HasColumnType("decimal(4,2)");
            e.Property(x => x.LastUpdated).HasColumnName("last_updated");
            e.HasOne(x => x.Profile).WithMany(p => p.WatchStates).HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.MediaItem).WithMany(mi => mi.WatchStates).HasForeignKey(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Episode).WithMany(ep => ep.WatchStates).HasForeignKey(x => x.EpisodeId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Season).WithMany().HasForeignKey(x => x.SeasonId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Movie).WithMany(mv => mv.WatchStates).HasForeignKey(x => x.MovieId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.ProfileId, x.MediaItemId, x.EpisodeId, x.SeasonId, x.MovieId }).IsUnique();
        });

        // ── ProfileNote ──
        modelBuilder.Entity<ProfileNote>(e =>
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
        });

        // ── SyncJob ──
        modelBuilder.Entity<SyncJob>(e =>
        {
            e.ToTable("sync_job");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.ProfileId).HasColumnName("profile_id");
            e.Property(x => x.StartedAt).HasColumnName("started_at");
            e.Property(x => x.CompletedAt).HasColumnName("completed_at");
            e.Property(x => x.ItemsProcessed).HasColumnName("items_processed");
            e.Property(x => x.ErrorMessage).HasColumnName("error_message");
            e.HasOne(x => x.Profile).WithMany().HasForeignKey(x => x.ProfileId).OnDelete(DeleteBehavior.SetNull);
        });

        // ── WebhookEventLog ──
        modelBuilder.Entity<WebhookEventLog>(e =>
        {
            e.ToTable("webhook_event_log");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.RawPayload).HasColumnName("raw_payload");
            e.Property(x => x.EventType).HasColumnName("event_type");
            e.Property(x => x.ReceivedAt).HasColumnName("received_at");
            e.Property(x => x.ProcessedAt).HasColumnName("processed_at");
            e.Property(x => x.Success).HasColumnName("success");
            e.Property(x => x.ErrorMessage).HasColumnName("error_message");
        });

        // ── ImportQueueItem ──
        modelBuilder.Entity<ImportQueueItem>(e =>
        {
            e.ToTable("import_queue_item");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.JellyfinItemId).HasColumnName("jellyfin_item_id");
            e.Property(x => x.MediaType).HasColumnName("media_type");
            e.Property(x => x.Priority).HasColumnName("priority");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.RetryCount).HasColumnName("retry_count");
            e.Property(x => x.NextRetryAt).HasColumnName("next_retry_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasIndex(x => x.JellyfinItemId);
            e.HasIndex(x => x.Status);
        });

        // ── MetadataRefreshJob ──
        modelBuilder.Entity<MetadataRefreshJob>(e =>
        {
            e.ToTable("metadata_refresh_job");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MediaItemId).HasColumnName("media_item_id");
            e.Property(x => x.Provider).HasColumnName("provider");
            e.Property(x => x.Status).HasColumnName("status");
            e.Property(x => x.LastRefreshed).HasColumnName("last_refreshed");
            e.Property(x => x.NextRefresh).HasColumnName("next_refresh");
            e.HasOne(x => x.MediaItem).WithMany().HasForeignKey(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
        });

        // ── MediaImage ──
        modelBuilder.Entity<MediaImage>(e =>
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
        });

        // ── MediaTranslation ──
        modelBuilder.Entity<MediaTranslation>(e =>
        {
            e.ToTable("media_translation");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.MediaItemId).HasColumnName("media_item_id");
            e.Property(x => x.Language).HasColumnName("language");
            e.Property(x => x.Title).HasColumnName("title");
            e.Property(x => x.Overview).HasColumnName("overview");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.HasOne(x => x.MediaItem).WithMany(mi => mi.Translations).HasForeignKey(x => x.MediaItemId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.MediaItemId, x.Language }).IsUnique();
        });

        // ── ProviderCacheEntry ──
        modelBuilder.Entity<ProviderCacheEntry>(e =>
        {
            e.ToTable("provider_cache_entry");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.Provider).HasColumnName("provider");
            e.Property(x => x.ExternalId).HasColumnName("external_id");
            e.Property(x => x.ResponseJson).HasColumnName("response_json");
            e.Property(x => x.CachedAt).HasColumnName("cached_at");
            e.Property(x => x.ExpiresAt).HasColumnName("expires_at");
            e.HasIndex(x => new { x.Provider, x.ExternalId }).IsUnique();
            e.HasIndex(x => x.ExpiresAt);
        });

        // ── JellyfinLibraryItem ──
        modelBuilder.Entity<JellyfinLibraryItem>(e =>
        {
            e.ToTable("jellyfin_library_item");
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.JellyfinItemId).HasColumnName("jellyfin_item_id");
            e.Property(x => x.JellyfinParentId).HasColumnName("jellyfin_parent_id");
            e.Property(x => x.Name).HasColumnName("name");
            e.Property(x => x.Type).HasColumnName("type");
            e.Property(x => x.MediaItemId).HasColumnName("media_item_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasOne(x => x.MediaItem).WithMany().HasForeignKey(x => x.MediaItemId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.JellyfinItemId).IsUnique();
        });
    }
}
