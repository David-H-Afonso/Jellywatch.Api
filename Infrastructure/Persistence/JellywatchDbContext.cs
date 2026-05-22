using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Infrastructure.Persistence;

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
    public DbSet<BackupSchedule> BackupSchedules { get; set; }
    public DbSet<Watchlist> Watchlists { get; set; }
    public DbSet<WatchlistMember> WatchlistMembers { get; set; }
    public DbSet<WatchlistItem> WatchlistItems { get; set; }
    public DbSet<WatchlistInvitation> WatchlistInvitations { get; set; }
    public DbSet<WatchlistAccessRequest> WatchlistAccessRequests { get; set; }
    public DbSet<UserWatchlistPreference> UserWatchlistPreferences { get; set; }

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
                else if (entry.Entity is Watchlist wl) { wl.CreatedAt = now; wl.UpdatedAt = now; }
                else if (entry.Entity is WatchlistMember wlm) { wlm.CreatedAt = now; wlm.UpdatedAt = now; }
                else if (entry.Entity is WatchlistItem wli) { wli.CreatedAt = now; wli.UpdatedAt = now; }
                else if (entry.Entity is WatchlistInvitation winv) { winv.CreatedAt = now; }
                else if (entry.Entity is WatchlistAccessRequest war) { war.CreatedAt = now; }
                else if (entry.Entity is UserWatchlistPreference uwp) { uwp.UpdatedAt = now; }
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
                else if (entry.Entity is Watchlist wl) { wl.UpdatedAt = now; }
                else if (entry.Entity is WatchlistMember wlm) { wlm.UpdatedAt = now; }
                else if (entry.Entity is WatchlistItem wli) { wli.UpdatedAt = now; }
                else if (entry.Entity is UserWatchlistPreference uwp) { uwp.UpdatedAt = now; }
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(JellywatchDbContext).Assembly);

        // SQLite stores DateTime as plain text with no timezone info.
        // Apply a global converter so every DateTime read from the DB is marked
        // as UTC — System.Text.Json then emits the "Z" suffix, and the browser
        // correctly converts to local time instead of treating the value as local.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            write => write,
            read => DateTime.SpecifyKind(read, DateTimeKind.Utc));
        var utcNullableConverter = new ValueConverter<DateTime?, DateTime?>(
            write => write,
            read => read.HasValue ? DateTime.SpecifyKind(read.Value, DateTimeKind.Utc) : null);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime))
                    property.SetValueConverter(utcConverter);
                else if (property.ClrType == typeof(DateTime?))
                    property.SetValueConverter(utcNullableConverter);
            }
        }
    }
}
