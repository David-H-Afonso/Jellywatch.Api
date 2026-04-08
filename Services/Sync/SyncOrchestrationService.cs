using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure;
using Jellywatch.Api.Services.Jellyfin;
using Jellywatch.Api.Services.Metadata;

namespace Jellywatch.Api.Services.Sync;

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
        // Find the matching local item
        var libraryItem = await _context.JellyfinLibraryItems
            .FirstOrDefaultAsync(li => li.JellyfinItemId == jellyfinItemId);

        int? mediaItemId = libraryItem?.MediaItemId;
        int? episodeId = null;
        int? movieId = null;

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
            var existing = await _context.ImportQueueItems
                .FirstOrDefaultAsync(q => q.JellyfinItemId == jellyfinItemId);

            if (existing == null)
            {
                _context.ImportQueueItems.Add(new ImportQueueItem
                {
                    JellyfinItemId = jellyfinItemId,
                    MediaType = MediaType.Series, // will be resolved during import
                    Priority = 1,
                    Status = ImportStatus.Pending,
                    RetryCount = 0,
                });
                await _context.SaveChangesAsync();
            }

            _logger.LogWarning("Jellyfin item {ItemId} not found locally, queued for import", jellyfinItemId);
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
            // For episodes, try to find via JellyfinItemId stored on the episode itself,
            // or fall back to finding any episode under this series' MediaItem
            var episode = await _context.Episodes
                .Include(e => e.Season)
                .Where(e => e.Season.Series.MediaItemId == mediaItemId.Value)
                .FirstOrDefaultAsync();

            if (episode != null)
                episodeId = episode.Id;
        }

        // Record the watch event (skip if a Finished event already exists)
        if (eventType == WatchEventType.Finished)
        {
            var alreadyExists = await _context.WatchEvents.AnyAsync(e =>
                e.ProfileId == profileId
                && e.MediaItemId == mediaItemId.Value
                && e.EpisodeId == episodeId
                && e.MovieId == movieId
                && e.EventType == WatchEventType.Finished);

            if (!alreadyExists)
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

            foreach (var profile in profiles)
            {
                itemsProcessed += await SyncProfileItemsAsync(profile);
            }

            syncJob.Status = SyncJobStatus.Completed;
            syncJob.CompletedAt = DateTime.UtcNow;
            syncJob.ItemsProcessed = itemsProcessed;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Full sync completed. Processed {Count} items for {Profiles} profiles", itemsProcessed, profiles.Count);
        }
        catch (Exception ex)
        {
            syncJob.Status = SyncJobStatus.Failed;
            syncJob.CompletedAt = DateTime.UtcNow;
            syncJob.ErrorMessage = ex.Message;
            await _context.SaveChangesAsync();

            _logger.LogError(ex, "Full sync failed");
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
            syncJob.Status = SyncJobStatus.Failed;
            syncJob.CompletedAt = DateTime.UtcNow;
            syncJob.ErrorMessage = ex.Message;
            await _context.SaveChangesAsync();
            throw;
        }
    }

    private async Task<int> SyncProfileItemsAsync(Profile profile)
    {
        int count = 0;

        // Get libraries from Jellyfin
        var libraries = await _jellyfinClient.GetLibrariesAsync(profile.JellyfinUserId);
        var mediaLibraries = libraries.Where(l =>
            l.CollectionType == "tvshows" || l.CollectionType == "movies").ToList();

        _logger.LogInformation("Syncing profile {ProfileId} across {LibCount} libraries: {Libs}",
            profile.Id, mediaLibraries.Count,
            string.Join(", ", mediaLibraries.Select(l => $"{l.Name} ({l.CollectionType})")));

        var updatedSeriesIds = new HashSet<int>();
        var updatedMovieIds = new HashSet<int>();
        var pendingPropagations = new List<(int MediaItemId, int? EpisodeId, int? MovieId, WatchState State)>();
        // Track items we've already tried to inline-import this sync pass
        var attemptedSeriesImports = new HashSet<string>();
        var attemptedMovieImports = new HashSet<string>();
        // Track all Jellyfin IDs seen in this sync for reconciliation
        var seenJellyfinSeriesIds = new HashSet<string>();
        var seenJellyfinMovieIds = new HashSet<string>();

        foreach (var library in mediaLibraries)
        {
            var itemType = library.CollectionType == "tvshows" ? "Episode" : "Movie";
            var items = await _jellyfinClient.GetItemsAsync(
                profile.JellyfinUserId,
                parentId: library.Id,
                itemTypes: itemType);

            _logger.LogInformation("Library '{LibName}' returned {ItemCount} {ItemType} items",
                library.Name, items.Count, itemType);

            // Record all seen Jellyfin IDs (regardless of watch state) for reconciliation
            foreach (var i in items)
            {
                if (i.Type == "Episode" && !string.IsNullOrWhiteSpace(i.SeriesId))
                    seenJellyfinSeriesIds.Add(i.SeriesId);
                else if (i.Type == "Movie")
                    seenJellyfinMovieIds.Add(i.Id);
            }

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
                        .FirstOrDefaultAsync(j => j.JellyfinItemId == item.SeriesId && j.MediaItemId != null);

                    if (seriesLink?.MediaItemId == null)
                    {
                        if (!attemptedSeriesImports.Contains(item.SeriesId))
                        {
                            attemptedSeriesImports.Add(item.SeriesId);
                            try
                            {
                                _logger.LogInformation("Inline-importing series '{Name}' ({SeriesId}) during sync",
                                    item.SeriesName ?? "Unknown", item.SeriesId);
                                var mediaItem = await _metadata.ResolveSeriesAsync(
                                    item.SeriesId, item.SeriesName ?? "Unknown");
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
                                .FirstOrDefaultAsync(j => j.JellyfinItemId == item.SeriesId && j.MediaItemId != null);
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

                    // Record a WatchEvent so the activity feed is populated
                    if (isNewOrChanged && state == WatchState.Seen)
                    {
                        var alreadyHasFinished = await _context.WatchEvents.AnyAsync(e =>
                            e.ProfileId == profile.Id && e.EpisodeId == episode.Id
                            && e.EventType == WatchEventType.Finished);
                        if (!alreadyHasFinished)
                        {
                            _context.WatchEvents.Add(new WatchEvent
                            {
                                ProfileId = profile.Id,
                                MediaItemId = seriesLink.MediaItemId.Value,
                                EpisodeId = episode.Id,
                                JellyfinItemId = item.Id,
                                EventType = WatchEventType.Finished,
                                Source = SyncSource.Polling,
                                Timestamp = item.UserData?.LastPlayedDate ?? DateTime.UtcNow,
                            });
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
                        .FirstOrDefaultAsync(j => j.JellyfinItemId == item.Id && j.MediaItemId != null);

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
                                .FirstOrDefaultAsync(j => j.JellyfinItemId == item.Id && j.MediaItemId != null);
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

                    // Record a WatchEvent so the activity feed is populated
                    if (isMovieNewOrChanged && state == WatchState.Seen)
                    {
                        var alreadyHasFinished = await _context.WatchEvents.AnyAsync(e =>
                            e.ProfileId == profile.Id && e.MovieId == movie.Id
                            && e.EventType == WatchEventType.Finished);
                        if (!alreadyHasFinished)
                        {
                            _context.WatchEvents.Add(new WatchEvent
                            {
                                ProfileId = profile.Id,
                                MediaItemId = movieLink.MediaItemId.Value,
                                MovieId = movie.Id,
                                JellyfinItemId = item.Id,
                                EventType = WatchEventType.Finished,
                                Source = SyncSource.Polling,
                                Timestamp = item.UserData?.LastPlayedDate ?? DateTime.UtcNow,
                            });
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
        // (watch history is preserved — only the Jellyfin link is removed)
        await ReconcileDeletedItemsAsync(seenJellyfinSeriesIds, seenJellyfinMovieIds);

        _logger.LogInformation("Sync summary for profile {ProfileId}: {EpCount} episodes ({SeriesCount} series), {MovCount} movies",
            profile.Id, count - updatedMovieIds.Count, updatedSeriesIds.Count, updatedMovieIds.Count);

        return count;
    }

    private async Task ReconcileDeletedItemsAsync(
        HashSet<string> seenSeriesIds,
        HashSet<string> seenMovieIds)
    {
        if (seenSeriesIds.Count == 0 && seenMovieIds.Count == 0) return;

        // Find JellyfinLibraryItems that are series/movies linked to a MediaItem,
        // but whose Jellyfin ID was NOT seen in the current sync pass
        var linkedSeries = await _context.JellyfinLibraryItems
            .Where(j => j.MediaItemId != null && j.Type == MediaType.Series)
            .ToListAsync();

        var linkedMovies = await _context.JellyfinLibraryItems
            .Where(j => j.MediaItemId != null && j.Type == MediaType.Movie)
            .ToListAsync();

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