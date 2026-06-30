using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure.Persistence;
using Jellywatch.Api.Infrastructure.ExternalServices;
using Jellywatch.Api.Application.Interfaces;

namespace Jellywatch.Api.Application.Services;

public class SyncOrchestrationService : ISyncOrchestrationService
{
    private readonly JellywatchDbContext _context;
    private readonly IJellyfinApiClient _jellyfinClient;
    private readonly IStateCalculationService _stateCalc;
    private readonly IPropagationService _propagation;
    private readonly IMetadataResolutionService _metadata;
    private readonly ILogger<SyncOrchestrationService> _logger;

    public SyncOrchestrationService(
        JellywatchDbContext context,
        IJellyfinApiClient jellyfinClient,
        IStateCalculationService stateCalc,
        IPropagationService propagation,
        IMetadataResolutionService metadata,
        ILogger<SyncOrchestrationService> logger)
    {
        _context = context;
        _jellyfinClient = jellyfinClient;
        _stateCalc = stateCalc;
        _propagation = propagation;
        _metadata = metadata;
        _logger = logger;
    }

    public async Task ProcessWatchEventAsync(int profileId, string jellyfinItemId, WatchEventType eventType, long positionTicks, SyncSource source)
    {
        var profile = await _context.Profiles.FindAsync(profileId);
        if (profile is null)
        {
            _logger.LogWarning("Skipping watch event for unknown profile {ProfileId}", profileId);
            return;
        }

        JellyfinItemInfo? jellyfinItem = null;
        try
        {
            jellyfinItem = await _jellyfinClient.GetItemAsync(jellyfinItemId, profile.JellyfinUserId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch Jellyfin item {ItemId} while processing watch event", jellyfinItemId);
        }

        // Find the matching local item
        var libraryItem = await _context.JellyfinLibraryItems
            .FirstOrDefaultAsync(li => li.JellyfinItemId == jellyfinItemId);

        int? mediaItemId = libraryItem?.MediaItemId;
        int? episodeId = null;
        int? movieId = null;

        if (mediaItemId == null
            && jellyfinItem?.Type == "Episode"
            && !string.IsNullOrWhiteSpace(jellyfinItem.SeriesId))
        {
            var seriesLink = await _context.JellyfinLibraryItems
                .FirstOrDefaultAsync(li => li.JellyfinItemId == jellyfinItem.SeriesId);
            mediaItemId = seriesLink?.MediaItemId;
        }

        if (mediaItemId == null)
        {
            // Skip if blacklisted
            var isBlacklisted = await _context.BlacklistedItems
                .AnyAsync(b => b.JellyfinItemId == jellyfinItemId);

            if (isBlacklisted)
            {
                _logger.LogDebug("Skipping blacklisted Jellyfin item {ItemId}", jellyfinItemId);
                return;
            }

            // Queue for import if not found
            var queueItemId = jellyfinItem?.Type == "Episode" && !string.IsNullOrWhiteSpace(jellyfinItem.SeriesId)
                ? jellyfinItem.SeriesId
                : jellyfinItemId;
            var existing = await _context.ImportQueueItems
                .FirstOrDefaultAsync(q => q.JellyfinItemId == queueItemId);

            if (existing == null)
            {
                _context.ImportQueueItems.Add(new ImportQueueItem
                {
                    JellyfinItemId = queueItemId,
                    MediaType = jellyfinItem?.Type == "Movie" ? MediaType.Movie : MediaType.Series,
                    Priority = 1,
                    Status = ImportStatus.Pending,
                    RetryCount = 0,
                });
                await _context.SaveChangesAsync();
            }

            _logger.LogWarning("Jellyfin item {ItemId} not found locally, queued {QueueItemId} for import", jellyfinItemId, queueItemId);
            return;
        }

        // Determine if this is an episode or movie
        // For episodes, the JellyfinLibraryItem maps the *series* Jellyfin ID to a MediaItemId.
        // The webhook sends the *episode* Jellyfin ID, so we first check if the item is a movie.
        // If not, we look up the episode via Jellyfin API metadata embedded in the library item.
        var movie = await _context.Movies.FirstOrDefaultAsync(m => m.MediaItemId == mediaItemId.Value);
        if (movie != null)
        {
            movieId = movie.Id;
        }
        else
        {
            if (jellyfinItem?.Type != "Episode"
                || !jellyfinItem.ParentIndexNumber.HasValue
                || !jellyfinItem.IndexNumber.HasValue)
            {
                _logger.LogWarning("Could not resolve episode metadata for Jellyfin item {ItemId}; skipping event", jellyfinItemId);
                return;
            }

            var episode = await _context.Episodes
                .Include(e => e.Season)
                .FirstOrDefaultAsync(e =>
                    e.Season.Series.MediaItemId == mediaItemId.Value
                    && e.Season.SeasonNumber == jellyfinItem.ParentIndexNumber.Value
                    && e.EpisodeNumber == jellyfinItem.IndexNumber.Value);

            if (episode is null && !string.IsNullOrWhiteSpace(jellyfinItem.SeriesId))
            {
                try
                {
                    var jellyfinEpisodes = await _jellyfinClient.GetItemsAsync(
                        profile.JellyfinUserId,
                        parentId: jellyfinItem.SeriesId,
                        itemTypes: "Episode");
                    var orderedJellyfinEpisodes = jellyfinEpisodes
                        .Where(e => e.ParentIndexNumber.HasValue && e.IndexNumber.HasValue && e.ParentIndexNumber.Value > 0)
                        .OrderBy(e => e.ParentIndexNumber!.Value)
                        .ThenBy(e => e.IndexNumber!.Value)
                        .ToList();
                    var ordinal = orderedJellyfinEpisodes.FindIndex(e => e.Id == jellyfinItem.Id);
                    if (ordinal >= 0)
                    {
                        var orderedLocalEpisodes = await _context.Episodes
                            .Include(e => e.Season)
                            .Where(e => e.Season.Series.MediaItemId == mediaItemId.Value && e.Season.SeasonNumber > 0)
                            .OrderBy(e => e.Season.SeasonNumber)
                            .ThenBy(e => e.EpisodeNumber)
                            .ToListAsync();
                        if (ordinal < orderedLocalEpisodes.Count)
                        {
                            episode = orderedLocalEpisodes[ordinal];
                            _logger.LogInformation(
                                "Matched realtime Jellyfin episode {JellyfinSeason}x{JellyfinEpisode} to local {LocalSeason}x{LocalEpisode} by absolute order for media {MediaItemId}",
                                jellyfinItem.ParentIndexNumber,
                                jellyfinItem.IndexNumber,
                                episode.Season.SeasonNumber,
                                episode.EpisodeNumber,
                                mediaItemId.Value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not build absolute episode fallback for Jellyfin item {ItemId}", jellyfinItemId);
                }
            }

            if (episode is null)
            {
                _logger.LogWarning(
                    "Could not match Jellyfin episode {Season}x{Episode} ({ItemId}) to local media {MediaItemId}; skipping event",
                    jellyfinItem.ParentIndexNumber,
                    jellyfinItem.IndexNumber,
                    jellyfinItemId,
                    mediaItemId.Value);
                return;
            }

            episodeId = episode.Id;
        }

        // Record the watch event; for Finished events, update an existing polling-created record
        // if it exists — polling uses LastPlayedDate (session start), the real-time webhook has
        // the accurate stop timestamp.
        if (eventType == WatchEventType.Finished)
        {
            var existingFinished = await _context.WatchEvents
                .FirstOrDefaultAsync(e =>
                    e.ProfileId == profileId
                    && e.MediaItemId == mediaItemId.Value
                    && e.EpisodeId == episodeId
                    && e.MovieId == movieId
                    && e.EventType == WatchEventType.Finished);

            if (existingFinished == null)
            {
                _context.WatchEvents.Add(new WatchEvent
                {
                    ProfileId = profileId,
                    MediaItemId = mediaItemId.Value,
                    EpisodeId = episodeId,
                    MovieId = movieId,
                    JellyfinItemId = jellyfinItemId,
                    EventType = eventType,
                    PositionTicks = positionTicks,
                    Source = source,
                    Timestamp = DateTime.UtcNow,
                });
            }
            else if (existingFinished.Source == SyncSource.Polling)
            {
                // Real-time webhook has the accurate stop time; correct the polling timestamp
                existingFinished.Timestamp = DateTime.UtcNow;
                existingFinished.Source = source;
            }
            await _context.SaveChangesAsync();
        }
        else
        {
            _context.WatchEvents.Add(new WatchEvent
            {
                ProfileId = profileId,
                MediaItemId = mediaItemId.Value,
                EpisodeId = episodeId,
                MovieId = movieId,
                JellyfinItemId = jellyfinItemId,
                EventType = eventType,
                PositionTicks = positionTicks,
                Source = source,
                Timestamp = DateTime.UtcNow,
            });
            await _context.SaveChangesAsync();
        }

        // Real-time Jellyfin event always takes precedence over manual overrides
        var existingState = await _context.ProfileWatchStates
            .FirstOrDefaultAsync(s => s.ProfileId == profileId && s.MediaItemId == mediaItemId.Value
                && s.EpisodeId == episodeId && s.MovieId == movieId);
        if (existingState?.IsManualOverride == true)
        {
            existingState.IsManualOverride = false;
            await _context.SaveChangesAsync();
        }

        // Recalculate state
        await _stateCalc.RecalculateProfileWatchStateAsync(profileId, mediaItemId.Value, episodeId, movieId);

        // Get the new state and propagate
        var newState = await _context.ProfileWatchStates
            .Where(s => s.ProfileId == profileId && s.MediaItemId == mediaItemId.Value && s.EpisodeId == episodeId && s.MovieId == movieId)
            .Select(s => s.State)
            .FirstOrDefaultAsync();

        await _propagation.PropagateStateChangeAsync(profileId, mediaItemId.Value, episodeId, movieId, newState);

        _logger.LogInformation("Processed {EventType} for profile {ProfileId} item {ItemId}", eventType, profileId, jellyfinItemId);
    }

    public async Task RunFullSyncAsync(int? profileId = null)
    {
        var syncJob = new SyncJob
        {
            Type = profileId.HasValue ? SyncJobType.Profile : SyncJobType.Full,
            Status = SyncJobStatus.Running,
            ProfileId = profileId,
            StartedAt = DateTime.UtcNow,
        };
        _context.SyncJobs.Add(syncJob);
        await _context.SaveChangesAsync();

        try
        {
            var profiles = profileId.HasValue
                ? await _context.Profiles.Where(p => p.Id == profileId.Value).ToListAsync()
                : await _context.Profiles.ToListAsync();

            int itemsProcessed = 0;
            var failedProfiles = new List<int>();

            foreach (var profile in profiles)
            {
                try
                {
                    itemsProcessed += await SyncProfileItemsAsync(profile);
                }
                catch (Exception ex)
                {
                    // Isolate per-profile failures: one profile (or one of its libraries)
                    // throwing must not abort the sync for everyone else.
                    failedProfiles.Add(profile.Id);
                    _logger.LogError(ex,
                        "Sync failed for profile {ProfileId} ({Name}); continuing with remaining profiles",
                        profile.Id, profile.DisplayName);
                    DetachPendingChanges(syncJob);
                }
            }

            // Only fail the whole job when every profile failed; otherwise it's a partial success.
            if (failedProfiles.Count > 0 && failedProfiles.Count == profiles.Count)
                throw new InvalidOperationException($"Sync failed for all {profiles.Count} profile(s).");

            syncJob.Status = SyncJobStatus.Completed;
            syncJob.CompletedAt = DateTime.UtcNow;
            syncJob.ItemsProcessed = itemsProcessed;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Full sync completed. Processed {Count} items for {Profiles} profiles ({Failed} failed)",
                itemsProcessed, profiles.Count, failedProfiles.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Full sync failed");
            await TryMarkSyncJobFailedAsync(syncJob, ex);
            throw;
        }
    }

    public async Task ReconcileProfileAsync(int profileId)
    {
        var syncJob = new SyncJob
        {
            Type = SyncJobType.Reconcile,
            Status = SyncJobStatus.Running,
            ProfileId = profileId,
            StartedAt = DateTime.UtcNow,
        };
        _context.SyncJobs.Add(syncJob);
        await _context.SaveChangesAsync();

        try
        {
            var profile = await _context.Profiles.FindAsync(profileId)
                ?? throw new KeyNotFoundException($"Profile {profileId} not found");

            var count = await SyncProfileItemsAsync(profile);

            syncJob.Status = SyncJobStatus.Completed;
            syncJob.CompletedAt = DateTime.UtcNow;
            syncJob.ItemsProcessed = count;
            await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            await TryMarkSyncJobFailedAsync(syncJob, ex);
            throw;
        }
    }

    private void DetachPendingChanges(SyncJob keep)
    {
        // After a failed/aborted operation EF keeps the offending Added/Modified/Deleted
        // entities tracked. Detach them (except the sync job itself) before continuing,
        // otherwise the next SaveChanges retries the same invalid write and hides the real error.
        foreach (var entry in _context.ChangeTracker.Entries()
                     .Where(e => !ReferenceEquals(e.Entity, keep)
                         && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                     .ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private async Task TryMarkSyncJobFailedAsync(SyncJob syncJob, Exception exception)
    {
        DetachPendingChanges(syncJob);

        syncJob.Status = SyncJobStatus.Failed;
        syncJob.CompletedAt = DateTime.UtcNow;
        syncJob.ErrorMessage = exception.GetBaseException().Message;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception saveException)
        {
            _logger.LogError(saveException, "Failed to persist failed sync job {SyncJobId}", syncJob.Id);
        }
    }

    // Jellyfin CollectionType slugs that are NOT video libraries we sync.
    private static readonly HashSet<string> NonVideoCollectionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "music", "musicvideos", "audiobooks", "books", "photos",
        "homevideos", "livetv", "playlists", "boxsets", "trailers", "folders"
    };

    // A library is syncable when it is movies/tvshows or a mixed/unset library (CollectionType
    // null or empty). Renaming a library can drop or change its CollectionType, so we must not
    // require an exact "movies"/"tvshows" match here.
    private static bool IsSyncableVideoLibrary(JellyfinLibraryInfo library)
    {
        var collectionType = library.CollectionType;
        if (string.IsNullOrWhiteSpace(collectionType)) return true;
        return !NonVideoCollectionTypes.Contains(collectionType);
    }

    private async Task<int> SyncProfileItemsAsync(Profile profile)
    {
        int count = 0;

        // Get libraries from Jellyfin. Log EVERY library with its raw CollectionType so a
        // renamed/mixed library that no longer matches the expected slug is visible here.
        var libraries = await _jellyfinClient.GetLibrariesAsync(profile.JellyfinUserId);
        _logger.LogInformation("Profile {ProfileId}: Jellyfin returned {Count} libraries: {Libs}",
            profile.Id, libraries.Count,
            string.Join(", ", libraries.Select(l => $"{l.Name} [{l.CollectionType ?? "null"}]")));

        // Sync the canonical "movies"/"tvshows" libraries plus any mixed/unset library
        // (e.g. after a rename, or a genuine "mixed content" library); each item's own Type
        // decides how it is handled below. Known non-video libraries are excluded.
        var mediaLibraries = libraries.Where(IsSyncableVideoLibrary).ToList();
        var excludedLibraries = libraries.Where(l => !IsSyncableVideoLibrary(l)).ToList();
        if (excludedLibraries.Count > 0)
            _logger.LogInformation("Profile {ProfileId}: skipping {Count} non-video libraries: {Libs}",
                profile.Id, excludedLibraries.Count,
                string.Join(", ", excludedLibraries.Select(l => $"{l.Name} [{l.CollectionType ?? "null"}]")));

        _logger.LogInformation("Syncing profile {ProfileId} across {LibCount} video libraries: {Libs}",
            profile.Id, mediaLibraries.Count,
            string.Join(", ", mediaLibraries.Select(l => $"{l.Name} [{l.CollectionType ?? "null"}]")));

        var updatedSeriesIds = new HashSet<int>();
        var updatedMovieIds = new HashSet<int>();
        var pendingPropagations = new List<(int MediaItemId, int? EpisodeId, int? MovieId, WatchState State)>();
        // Track items we've already tried to inline-import this sync pass
        var attemptedSeriesImports = new HashSet<string>();
        var attemptedMovieImports = new HashSet<string>();
        // Track all Jellyfin IDs seen in this sync for reconciliation
        var seenJellyfinSeriesIds = new HashSet<string>();
        var seenJellyfinMovieIds = new HashSet<string>();
        // Count libraries successfully read per media type. Reconciliation (unlinking) must
        // only run for a type when at least one of its libraries was actually read — a missing,
        // renamed, or failed library must never be treated as "all items deleted".
        int seriesLibrariesRead = 0;
        int movieLibrariesRead = 0;

        // Fetch Jellyfin's activity log once to get accurate VideoPlaybackStopped times.
        // Jellyfin's UserData.LastPlayedDate is the session-start time, not the finish time;
        // the activity log has the real stop timestamp for each item.
        var stopTimes = new Dictionary<string, DateTime>();
        try
        {
            var activityLog = await _jellyfinClient.GetActivityLogAsync(
                minDate: DateTime.UtcNow.AddDays(-90), limit: 2000);
            stopTimes = activityLog
                .Where(a => a.Type == "VideoPlaybackStopped"
                         && a.ItemId != null
                         && a.UserId == profile.JellyfinUserId)
                .GroupBy(a => a.ItemId!)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(a => a.Date).First().Date);
            _logger.LogInformation("Activity log fetched for profile {ProfileId}: {Count} stop events",
                profile.Id, stopTimes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch activity log for profile {ProfileId} — timestamps will use fallback",
                profile.Id);
        }

        foreach (var library in mediaLibraries)
        {
            // Movies → "Movie", Shows → "Episode", mixed/unknown → fetch both and let each
            // item's Type drive handling below.
            var collectionType = library.CollectionType;
            var isMoviesLib = string.Equals(collectionType, "movies", StringComparison.OrdinalIgnoreCase);
            var isShowsLib = string.Equals(collectionType, "tvshows", StringComparison.OrdinalIgnoreCase);
            var itemType = isShowsLib ? "Episode" : isMoviesLib ? "Movie" : "Movie,Episode";

            List<JellyfinItemInfo> items;
            try
            {
                items = await _jellyfinClient.GetItemsAsync(
                    profile.JellyfinUserId,
                    parentId: library.Id,
                    itemTypes: itemType);
            }
            catch (Exception ex)
            {
                // A single library failing (e.g. Jellyfin returns 500 while it rescans after a
                // rename) must NOT abort the whole sync and must NOT make this library's items
                // look "deleted" during reconciliation — so we skip without counting it as read.
                _logger.LogWarning(ex,
                    "Failed to read library '{LibName}' [{CollectionType}] for profile {ProfileId}; skipping (links preserved)",
                    library.Name, collectionType ?? "null", profile.Id);
                continue;
            }

            // Library read OK → its media type(s) are safe to reconcile later.
            if (isShowsLib || string.IsNullOrWhiteSpace(collectionType)) seriesLibrariesRead++;
            if (isMoviesLib || string.IsNullOrWhiteSpace(collectionType)) movieLibrariesRead++;

            _logger.LogInformation("Library '{LibName}' [{CollectionType}] returned {ItemCount} items ({ItemType})",
                library.Name, collectionType ?? "null", items.Count, itemType);

            var jellyfinEpisodeOrdinalBySeries = items
                .Where(i => i.Type == "Episode"
                    && !string.IsNullOrWhiteSpace(i.SeriesId)
                    && i.ParentIndexNumber.HasValue
                    && i.IndexNumber.HasValue
                    && i.ParentIndexNumber.Value > 0)
                .GroupBy(i => i.SeriesId!)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(i => i.ParentIndexNumber!.Value)
                        .ThenBy(i => i.IndexNumber!.Value)
                        .Select((episode, index) => new { episode.Id, index })
                        .ToDictionary(x => x.Id, x => x.index));
            var localEpisodeCache = new Dictionary<int, List<Episode>>();

            // Record all seen Jellyfin IDs (regardless of watch state) for reconciliation
            foreach (var i in items)
            {
                if (i.Type == "Episode" && !string.IsNullOrWhiteSpace(i.SeriesId))
                    seenJellyfinSeriesIds.Add(i.SeriesId);
                else if (i.Type == "Movie")
                    seenJellyfinMovieIds.Add(i.Id);
            }

            // Restore links for movies that already exist locally but are unlinked (e.g. every
            // movie after the "Películas" → "Movies" rename severed them). This does NOT depend
            // on the movie having been watched, so watchlist → Jellyfin playlist sync works again.
            await RelinkExistingMoviesAsync(items);

            int libraryCount = 0;
            int skippedCount = 0;

            foreach (var item in items)
            {
                if (item.UserData == null) continue;

                var played = item.UserData.Played;
                var positionTicks = item.UserData.PlaybackPositionTicks;

                if (!played && positionTicks == 0) continue;

                if (item.Type == "Episode"
                    && !string.IsNullOrWhiteSpace(item.SeriesId)
                    && item.IndexNumber.HasValue
                    && item.ParentIndexNumber.HasValue)
                {
                    // ── Episode path ──
                    var seriesLink = await _context.JellyfinLibraryItems
                        .FirstOrDefaultAsync(j => j.JellyfinItemId == item.SeriesId);

                    if (seriesLink?.MediaItemId == null)
                    {
                        if (!attemptedSeriesImports.Contains(item.SeriesId))
                        {
                            attemptedSeriesImports.Add(item.SeriesId);
                            try
                            {
                                var seriesName = item.SeriesName ?? "Unknown";
                                int? tmdbId = null;
                                string? imdbId = null;
                                int? year = null;
                                try
                                {
                                    var jellyfinSeries = await _jellyfinClient.GetItemAsync(item.SeriesId, profile.JellyfinUserId);
                                    if (jellyfinSeries is not null)
                                    {
                                        seriesName = string.IsNullOrWhiteSpace(jellyfinSeries.Name) ? seriesName : jellyfinSeries.Name;
                                        if (jellyfinSeries.ProviderIds is not null)
                                        {
                                            if (jellyfinSeries.ProviderIds.TryGetValue("Tmdb", out var tmdbStr) && int.TryParse(tmdbStr, out var parsedTmdb))
                                                tmdbId = parsedTmdb;
                                            if (jellyfinSeries.ProviderIds.TryGetValue("Imdb", out var imdbStr))
                                                imdbId = imdbStr;
                                        }
                                        if (!string.IsNullOrWhiteSpace(jellyfinSeries.PremiereDate) && DateTime.TryParse(jellyfinSeries.PremiereDate, out var premiere))
                                            year = premiere.Year;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Could not fetch Jellyfin series metadata for {SeriesId}; falling back to episode metadata", item.SeriesId);
                                }

                                _logger.LogInformation("Inline-importing series '{Name}' ({SeriesId}) during sync",
                                    seriesName, item.SeriesId);
                                var mediaItem = await _metadata.ResolveSeriesAsync(
                                    item.SeriesId, seriesName, year, tmdbId, imdbId);
                                if (mediaItem != null)
                                {
                                    var series = await _context.Series
                                        .FirstOrDefaultAsync(s => s.MediaItemId == mediaItem.Id);
                                    if (series != null)
                                        await _metadata.PopulateSeasonsAndEpisodesAsync(series.Id);
                                    await _metadata.RefreshTranslationsAsync(mediaItem.Id);
                                    await _metadata.RefreshImagesAsync(mediaItem.Id);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Inline import of series {SeriesId} failed — skipping", item.SeriesId);
                            }
                            // Re-query after import
                            seriesLink = await _context.JellyfinLibraryItems
                                .FirstOrDefaultAsync(j => j.JellyfinItemId == item.SeriesId);
                        }

                        if (seriesLink?.MediaItemId == null)
                        {
                            skippedCount++;
                            continue;
                        }
                    }

                    // Find local episode by season/episode number
                    var episode = await _context.Episodes
                        .Include(e => e.Season)
                        .FirstOrDefaultAsync(e =>
                            e.Season.Series.MediaItemId == seriesLink.MediaItemId.Value
                            && e.Season.SeasonNumber == item.ParentIndexNumber.Value
                            && e.EpisodeNumber == item.IndexNumber.Value);

                    if (episode == null
                        && jellyfinEpisodeOrdinalBySeries.TryGetValue(item.SeriesId, out var ordinalByItemId)
                        && ordinalByItemId.TryGetValue(item.Id, out var ordinal))
                    {
                        if (!localEpisodeCache.TryGetValue(seriesLink.MediaItemId.Value, out var orderedLocalEpisodes))
                        {
                            orderedLocalEpisodes = await _context.Episodes
                                .Include(e => e.Season)
                                .Where(e => e.Season.Series.MediaItemId == seriesLink.MediaItemId.Value
                                    && e.Season.SeasonNumber > 0)
                                .OrderBy(e => e.Season.SeasonNumber)
                                .ThenBy(e => e.EpisodeNumber)
                                .ToListAsync();
                            if (orderedLocalEpisodes.Count == 0)
                            {
                                orderedLocalEpisodes = await _context.Episodes
                                    .Include(e => e.Season)
                                    .Where(e => e.Season.Series.MediaItemId == seriesLink.MediaItemId.Value)
                                    .OrderBy(e => e.Season.SeasonNumber)
                                    .ThenBy(e => e.EpisodeNumber)
                                    .ToListAsync();
                            }
                            localEpisodeCache[seriesLink.MediaItemId.Value] = orderedLocalEpisodes;
                        }

                        if (ordinal >= 0 && ordinal < orderedLocalEpisodes.Count)
                        {
                            episode = orderedLocalEpisodes[ordinal];
                            _logger.LogInformation(
                                "Matched Jellyfin episode {JellyfinSeason}x{JellyfinEpisode} to local {LocalSeason}x{LocalEpisode} by absolute order for media {MediaItemId}",
                                item.ParentIndexNumber,
                                item.IndexNumber,
                                episode.Season.SeasonNumber,
                                episode.EpisodeNumber,
                                seriesLink.MediaItemId.Value);
                        }
                    }

                    if (episode == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    var state = _stateCalc.CalculateEpisodeState(played, positionTicks, null);

                    var existing = await _context.ProfileWatchStates
                        .FirstOrDefaultAsync(s => s.ProfileId == profile.Id && s.EpisodeId == episode.Id);

                    var isNewOrChanged = false;
                    if (existing == null)
                    {
                        _context.ProfileWatchStates.Add(new ProfileWatchState
                        {
                            ProfileId = profile.Id,
                            MediaItemId = seriesLink.MediaItemId.Value,
                            EpisodeId = episode.Id,
                            State = state,
                            LastUpdated = DateTime.UtcNow,
                        });
                        isNewOrChanged = true;
                    }
                    else if (existing.State != state)
                    {
                        // If user manually overrode, only update if Jellyfin has newer activity
                        if (existing.IsManualOverride)
                        {
                            var jellyfinDate = item.UserData?.LastPlayedDate;
                            if (jellyfinDate == null || jellyfinDate <= existing.LastUpdated)
                            {
                                skippedCount++;
                                continue;
                            }
                        }
                        existing.State = state;
                        existing.IsManualOverride = false;
                        existing.LastUpdated = DateTime.UtcNow;
                        isNewOrChanged = true;
                    }

                    // Record/correct the Finished WatchEvent for the activity feed.
                    // Timestamp priority: webhook Stopped event > Jellyfin activity log > LastPlayedDate > UtcNow
                    if (state == WatchState.Seen)
                    {
                        var stoppedWebhookEvent = await _context.WatchEvents
                            .Where(e => e.ProfileId == profile.Id
                                     && e.EpisodeId == episode.Id
                                     && e.EventType == WatchEventType.Stopped)
                            .OrderByDescending(e => e.Timestamp)
                            .FirstOrDefaultAsync();
                        var activityLogTime = stopTimes.TryGetValue(item.Id, out var aDate) ? aDate : (DateTime?)null;
                        var bestTimestamp = stoppedWebhookEvent?.Timestamp
                            ?? activityLogTime
                            ?? item.UserData?.LastPlayedDate
                            ?? DateTime.UtcNow;

                        var existingFinished = await _context.WatchEvents
                            .FirstOrDefaultAsync(e => e.ProfileId == profile.Id
                                && e.EpisodeId == episode.Id
                                && e.EventType == WatchEventType.Finished);

                        if (existingFinished == null)
                        {
                            if (isNewOrChanged)
                            {
                                _context.WatchEvents.Add(new WatchEvent
                                {
                                    ProfileId = profile.Id,
                                    MediaItemId = seriesLink.MediaItemId.Value,
                                    EpisodeId = episode.Id,
                                    JellyfinItemId = item.Id,
                                    EventType = WatchEventType.Finished,
                                    Source = SyncSource.Polling,
                                    Timestamp = bestTimestamp,
                                });
                            }
                        }
                        else if (existingFinished.Source == SyncSource.Polling && activityLogTime.HasValue
                                 && existingFinished.Timestamp != activityLogTime.Value)
                        {
                            // Correct the inaccurate polling timestamp with the activity log's real stop time
                            existingFinished.Timestamp = activityLogTime.Value;
                        }
                    }

                    updatedSeriesIds.Add(seriesLink.MediaItemId.Value);
                    pendingPropagations.Add((seriesLink.MediaItemId.Value, episode.Id, null, state));
                    libraryCount++;
                }
                else if (item.Type == "Movie")
                {
                    // ── Movie path ──
                    var movieLink = await _context.JellyfinLibraryItems
                        .FirstOrDefaultAsync(j => j.JellyfinItemId == item.Id);

                    if (movieLink?.MediaItemId == null)
                    {
                        if (!attemptedMovieImports.Contains(item.Id))
                        {
                            attemptedMovieImports.Add(item.Id);
                            try
                            {
                                int? tmdbId = null;
                                string? imdbId = null;
                                int? year = null;
                                if (item.ProviderIds != null)
                                {
                                    if (item.ProviderIds.TryGetValue("Tmdb", out var tmdbStr) && int.TryParse(tmdbStr, out var parsed))
                                        tmdbId = parsed;
                                    if (item.ProviderIds.TryGetValue("Imdb", out var imdb))
                                        imdbId = imdb;
                                }
                                if (!string.IsNullOrWhiteSpace(item.PremiereDate) && DateTime.TryParse(item.PremiereDate, out var premiere))
                                    year = premiere.Year;

                                _logger.LogInformation("Inline-importing movie '{Name}' ({MovieId}) during sync",
                                    item.Name, item.Id);
                                var mediaItem = await _metadata.ResolveMovieAsync(item.Id, item.Name, year, tmdbId, imdbId);
                                if (mediaItem != null)
                                {
                                    await _metadata.RefreshTranslationsAsync(mediaItem.Id);
                                    await _metadata.RefreshImagesAsync(mediaItem.Id);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Inline import of movie '{Name}' ({MovieId}) failed — skipping",
                                    item.Name, item.Id);
                            }
                            // Re-query after import
                            movieLink = await _context.JellyfinLibraryItems
                                .FirstOrDefaultAsync(j => j.JellyfinItemId == item.Id);
                        }

                        if (movieLink?.MediaItemId == null)
                        {
                            skippedCount++;
                            continue;
                        }
                    }

                    var movie = await _context.Movies
                        .FirstOrDefaultAsync(m => m.MediaItemId == movieLink.MediaItemId.Value);

                    if (movie == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    var state = _stateCalc.CalculateMovieState(played, positionTicks, item.RunTimeTicks);

                    var existing = await _context.ProfileWatchStates
                        .FirstOrDefaultAsync(s => s.ProfileId == profile.Id && s.MovieId == movie.Id);

                    var isMovieNewOrChanged = false;
                    if (existing == null)
                    {
                        _context.ProfileWatchStates.Add(new ProfileWatchState
                        {
                            ProfileId = profile.Id,
                            MediaItemId = movieLink.MediaItemId.Value,
                            MovieId = movie.Id,
                            State = state,
                            LastUpdated = DateTime.UtcNow,
                        });
                        isMovieNewOrChanged = true;
                    }
                    else if (existing.State != state)
                    {
                        if (existing.IsManualOverride)
                        {
                            var jellyfinDate = item.UserData?.LastPlayedDate;
                            if (jellyfinDate == null || jellyfinDate <= existing.LastUpdated)
                            {
                                skippedCount++;
                                continue;
                            }
                        }
                        existing.State = state;
                        existing.IsManualOverride = false;
                        existing.LastUpdated = DateTime.UtcNow;
                        isMovieNewOrChanged = true;
                    }

                    // Record/correct the Finished WatchEvent for the activity feed.
                    // Timestamp priority: webhook Stopped event > Jellyfin activity log > LastPlayedDate > UtcNow
                    if (state == WatchState.Seen)
                    {
                        var stoppedWebhookEvent = await _context.WatchEvents
                            .Where(e => e.ProfileId == profile.Id
                                     && e.MovieId == movie.Id
                                     && e.EventType == WatchEventType.Stopped)
                            .OrderByDescending(e => e.Timestamp)
                            .FirstOrDefaultAsync();
                        var activityLogTime = stopTimes.TryGetValue(item.Id, out var mDate) ? mDate : (DateTime?)null;
                        var bestTimestamp = stoppedWebhookEvent?.Timestamp
                            ?? activityLogTime
                            ?? item.UserData?.LastPlayedDate
                            ?? DateTime.UtcNow;

                        var existingFinished = await _context.WatchEvents
                            .FirstOrDefaultAsync(e => e.ProfileId == profile.Id
                                && e.MovieId == movie.Id
                                && e.EventType == WatchEventType.Finished);

                        if (existingFinished == null)
                        {
                            if (isMovieNewOrChanged)
                            {
                                _context.WatchEvents.Add(new WatchEvent
                                {
                                    ProfileId = profile.Id,
                                    MediaItemId = movieLink.MediaItemId.Value,
                                    MovieId = movie.Id,
                                    JellyfinItemId = item.Id,
                                    EventType = WatchEventType.Finished,
                                    Source = SyncSource.Polling,
                                    Timestamp = bestTimestamp,
                                });
                            }
                        }
                        else if (existingFinished.Source == SyncSource.Polling && activityLogTime.HasValue
                                 && existingFinished.Timestamp != activityLogTime.Value)
                        {
                            // Correct the inaccurate polling timestamp with the activity log's real stop time
                            existingFinished.Timestamp = activityLogTime.Value;
                        }
                    }

                    updatedMovieIds.Add(movieLink.MediaItemId.Value);
                    pendingPropagations.Add((movieLink.MediaItemId.Value, null, movie.Id, state));
                    libraryCount++;
                }
                else
                {
                    _logger.LogDebug("Skipping unsupported item type '{Type}' for '{Name}' ({Id})",
                        item.Type, item.Name, item.Id);
                    skippedCount++;
                }
            }

            count += libraryCount;
            _logger.LogInformation("Library '{LibName}': processed {Processed}, skipped {Skipped}",
                library.Name, libraryCount, skippedCount);
        }

        await _context.SaveChangesAsync();

        // Recalculate series-level watch states for all updated series
        foreach (var mediaItemId in updatedSeriesIds)
            await _stateCalc.RecalculateProfileWatchStateAsync(profile.Id, mediaItemId);

        // Propagate state changes to other profiles that have rules from this one
        foreach (var (mediaItemId, episodeId, movieId, state) in pendingPropagations)
            await _propagation.PropagateStateChangeAsync(profile.Id, mediaItemId, episodeId, movieId, state);

        // Reconcile: unlink Jellyfin items that no longer exist in the user's library
        // (watch history is preserved — only the Jellyfin link is removed).
        // Guard per media type: only unlink a type when at least one of its libraries was
        // actually read AND returned items this pass. Without this guard a missing/renamed
        // library (e.g. "Películas" → "Movies") would wipe every link of that type.
        await ReconcileDeletedItemsAsync(
            seenJellyfinSeriesIds, seenJellyfinMovieIds,
            reconcileSeries: seriesLibrariesRead > 0 && seenJellyfinSeriesIds.Count > 0,
            reconcileMovies: movieLibrariesRead > 0 && seenJellyfinMovieIds.Count > 0);

        _logger.LogInformation("Sync summary for profile {ProfileId}: {EpCount} episodes ({SeriesCount} series), {MovCount} movies",
            profile.Id, count - updatedMovieIds.Count, updatedSeriesIds.Count, updatedMovieIds.Count);

        return count;
    }

    /// <summary>
    /// Restores jellyfin_library_item links for movies that already exist locally (matched by
    /// TMDb/IMDb) even when they were never watched. Repairs links a previous over-aggressive
    /// reconcile severed (e.g. all movies after the "Películas" → "Movies" rename) so that
    /// watchlist → Jellyfin playlist sync and "in your library" state work without playback.
    /// Cheap: only DB lookups against already-fetched items, no metadata/TMDb calls.
    /// </summary>
    private async Task RelinkExistingMoviesAsync(List<JellyfinItemInfo> items)
    {
        var movies = items.Where(i => i.Type == "Movie" && i.ProviderIds != null).ToList();
        if (movies.Count == 0) return;

        var tmdbIds = new HashSet<int>();
        var imdbIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in movies)
        {
            if (m.ProviderIds!.TryGetValue("Tmdb", out var t) && int.TryParse(t, out var ti)) tmdbIds.Add(ti);
            if (m.ProviderIds.TryGetValue("Imdb", out var im) && !string.IsNullOrWhiteSpace(im)) imdbIds.Add(im);
        }
        if (tmdbIds.Count == 0 && imdbIds.Count == 0) return;

        var localMovies = await _context.MediaItems
            .Where(mi => mi.MediaType == MediaType.Movie
                && ((mi.TmdbId != null && tmdbIds.Contains(mi.TmdbId.Value))
                    || (mi.ImdbId != null && imdbIds.Contains(mi.ImdbId))))
            .Select(mi => new { mi.Id, mi.TmdbId, mi.ImdbId })
            .ToListAsync();
        if (localMovies.Count == 0) return;

        var localByTmdb = localMovies.Where(x => x.TmdbId != null)
            .GroupBy(x => x.TmdbId!.Value).ToDictionary(g => g.Key, g => g.First().Id);
        var localByImdb = localMovies.Where(x => !string.IsNullOrWhiteSpace(x.ImdbId))
            .GroupBy(x => x.ImdbId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var jellyfinIds = movies.Select(m => m.Id).ToList();
        var existingLinks = (await _context.JellyfinLibraryItems
                .Where(j => jellyfinIds.Contains(j.JellyfinItemId))
                .ToListAsync())
            .GroupBy(j => j.JellyfinItemId)
            .ToDictionary(g => g.Key, g => g.First());

        int relinked = 0;
        foreach (var m in movies)
        {
            int? localId = null;
            if (m.ProviderIds!.TryGetValue("Tmdb", out var t) && int.TryParse(t, out var ti) && localByTmdb.TryGetValue(ti, out var idT))
                localId = idT;
            else if (m.ProviderIds.TryGetValue("Imdb", out var im) && !string.IsNullOrWhiteSpace(im) && localByImdb.TryGetValue(im, out var idI))
                localId = idI;
            if (localId == null) continue;

            if (existingLinks.TryGetValue(m.Id, out var link))
            {
                if (link.MediaItemId != localId)
                {
                    link.MediaItemId = localId;
                    link.Type = MediaType.Movie;
                    relinked++;
                }
            }
            else
            {
                _context.JellyfinLibraryItems.Add(new JellyfinLibraryItem
                {
                    JellyfinItemId = m.Id,
                    Name = m.Name,
                    Type = MediaType.Movie,
                    MediaItemId = localId,
                });
                relinked++;
            }
        }

        if (relinked > 0)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Relinked {Count} existing movie(s) to local media items (no playback required)", relinked);
        }
    }

    private async Task ReconcileDeletedItemsAsync(
        HashSet<string> seenSeriesIds,
        HashSet<string> seenMovieIds,
        bool reconcileSeries,
        bool reconcileMovies)
    {
        if (!reconcileSeries && !reconcileMovies) return;

        // Find JellyfinLibraryItems that are series/movies linked to a MediaItem,
        // but whose Jellyfin ID was NOT seen in the current sync pass. Only consider a media
        // type when its libraries were actually read this pass (see guard at the call site).
        var linkedSeries = reconcileSeries
            ? await _context.JellyfinLibraryItems
                .Where(j => j.MediaItemId != null && j.Type == MediaType.Series)
                .ToListAsync()
            : new List<JellyfinLibraryItem>();

        var linkedMovies = reconcileMovies
            ? await _context.JellyfinLibraryItems
                .Where(j => j.MediaItemId != null && j.Type == MediaType.Movie)
                .ToListAsync()
            : new List<JellyfinLibraryItem>();

        var deletedSeries = linkedSeries.Where(j => !seenSeriesIds.Contains(j.JellyfinItemId)).ToList();
        var deletedMovies = linkedMovies.Where(j => !seenMovieIds.Contains(j.JellyfinItemId)).ToList();

        if (deletedSeries.Count > 0 || deletedMovies.Count > 0)
        {
            foreach (var item in deletedSeries.Concat(deletedMovies))
            {
                _logger.LogInformation(
                    "Unlinking deleted Jellyfin item '{Name}' ({JellyfinId}) — watch history preserved",
                    item.Name, item.JellyfinItemId);
                item.MediaItemId = null;
            }
            await _context.SaveChangesAsync();
        }
    }
}