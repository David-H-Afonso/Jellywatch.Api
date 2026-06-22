using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure.ExternalServices;
using Jellywatch.Api.Infrastructure.Persistence;

namespace Jellywatch.Api.Application.Services;

public class JellyfinPlaylistSyncService : IJellyfinPlaylistSyncService
{
    private readonly JellywatchDbContext _context;
    private readonly IJellyfinApiClient _jellyfinClient;
    private readonly ILogger<JellyfinPlaylistSyncService> _logger;

    public JellyfinPlaylistSyncService(
        JellywatchDbContext context,
        IJellyfinApiClient jellyfinClient,
        ILogger<JellyfinPlaylistSyncService> logger)
    {
        _context = context;
        _jellyfinClient = jellyfinClient;
        _logger = logger;
    }

    public async Task<ServiceResult<PlaylistSyncPreviewDto>> PreviewSyncAsync(int watchlistId, int currentUserId)
    {
        var watchlist = await _context.Watchlists
            .Include(w => w.Items.OrderBy(i => i.Position))
                .ThenInclude(i => i.MediaItem)
            .FirstOrDefaultAsync(w => w.Id == watchlistId);

        if (watchlist is null)
            return ServiceResult<PlaylistSyncPreviewDto>.Fail("Watchlist not found", 404);

        var jellyfinMap = await BuildJellyfinMapAsync(watchlist.Items);

        var syncable = new List<PlaylistSyncItemDto>();
        var skipped = new List<PlaylistSkippedItemDto>();
        int syncPosition = 0;

        foreach (var item in watchlist.Items)
        {
            if (item.MediaItemId is null || item.MediaItem is null)
            {
                skipped.Add(new PlaylistSkippedItemDto(
                    item.MediaItem?.Title ?? "Unknown",
                    item.MediaItem?.MediaType.ToString() ?? "Unknown",
                    item.Position,
                    "No media item linked"));
                continue;
            }

            if (jellyfinMap.TryGetValue(item.MediaItemId.Value, out var jellyfinItemId))
            {
                syncable.Add(new PlaylistSyncItemDto(
                    item.MediaItem.Title,
                    item.MediaItem.MediaType.ToString(),
                    syncPosition++,
                    jellyfinItemId));
            }
            else
            {
                skipped.Add(new PlaylistSkippedItemDto(
                    item.MediaItem.Title,
                    item.MediaItem.MediaType.ToString(),
                    item.Position,
                    "Not in Jellyfin library"));
            }
        }

        // Get available profiles for playlist creation
        var currentUser = await _context.Users.FindAsync(currentUserId);
        var isAdmin = currentUser?.IsAdmin == true;

        var profiles = await _context.Profiles
            .Include(p => p.User)
            .Where(p => p.JellyfinUserId != "" && p.JellyfinUserId != null)
            .Where(p => isAdmin || p.UserId == currentUserId)
            .Select(p => new JellyfinTargetProfileDto(
                p.JellyfinUserId,
                p.DisplayName,
                p.User != null ? p.User.Username : ""))
            .ToListAsync();

        return ServiceResult<PlaylistSyncPreviewDto>.Ok(
            new PlaylistSyncPreviewDto(syncable, skipped, watchlist.Items.Count, profiles));
    }

    public async Task<ServiceResult> CreatePlaylistFromWatchlistAsync(int watchlistId, string jellyfinUserId)
    {
        var watchlist = await _context.Watchlists
            .Include(w => w.Items.OrderBy(i => i.Position))
            .FirstOrDefaultAsync(w => w.Id == watchlistId);

        if (watchlist is null)
            return ServiceResult.Fail("Watchlist not found", 404);

        if (watchlist.JellyfinPlaylistId is not null)
            return ServiceResult.Fail("Watchlist already linked to a Jellyfin playlist", 409);

        var jellyfinItemIds = await ResolveJellyfinItemIdsAsync(watchlist.Items, jellyfinUserId);

        if (jellyfinItemIds.Count == 0)
            return ServiceResult.Fail("No items could be resolved to Jellyfin library items", 400);

        var playlistId = await _jellyfinClient.CreatePlaylistAsync(
            watchlist.Name,
            jellyfinItemIds,
            jellyfinUserId);

        if (playlistId is null)
            return ServiceResult.Fail("Failed to create playlist in Jellyfin", 502);

        watchlist.JellyfinPlaylistId = playlistId;
        watchlist.JellyfinPlaylistUserId = jellyfinUserId;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created Jellyfin playlist {PlaylistId} for watchlist {WatchlistId}", playlistId, watchlistId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> SyncPlaylistAsync(int watchlistId)
    {
        var watchlist = await _context.Watchlists
            .Include(w => w.Items.OrderBy(i => i.Position))
            .FirstOrDefaultAsync(w => w.Id == watchlistId);

        if (watchlist is null)
            return ServiceResult.Fail("Watchlist not found", 404);

        if (watchlist.JellyfinPlaylistId is null)
            return ServiceResult.Fail("Watchlist is not linked to a Jellyfin playlist", 400);

        // Use the same Jellyfin user that originally created the playlist
        var jellyfinUserId = watchlist.JellyfinPlaylistUserId ?? await GetAnyJellyfinUserIdAsync();
        if (jellyfinUserId is null)
            return ServiceResult.Fail("No Jellyfin user configured", 400);

        var jellyfinItemIds = await ResolveJellyfinItemIdsAsync(watchlist.Items, jellyfinUserId);

        // Jellyfin 10.9+ requires user context for POST /Playlists/{id}.
        // Workaround: delete old playlist and recreate with new items.
        var oldPlaylistId = watchlist.JellyfinPlaylistId;
        await _jellyfinClient.DeletePlaylistAsync(oldPlaylistId);

        var newPlaylistId = await _jellyfinClient.CreatePlaylistAsync(
            watchlist.Name,
            jellyfinItemIds,
            jellyfinUserId);

        if (newPlaylistId is null)
        {
            // Old playlist is gone; clear stale reference
            watchlist.JellyfinPlaylistId = null;
            watchlist.JellyfinPlaylistUserId = null;
            await _context.SaveChangesAsync();
            _logger.LogError("Deleted old playlist {OldId} but failed to recreate for watchlist {WatchlistId}. Playlist unlinked.",
                oldPlaylistId, watchlistId);
            return ServiceResult.Fail("Failed to recreate playlist in Jellyfin. Playlist has been unlinked.", 502);
        }

        watchlist.JellyfinPlaylistId = newPlaylistId;
        watchlist.JellyfinPlaylistUserId = jellyfinUserId;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Re-synced Jellyfin playlist {PlaylistId} for watchlist {WatchlistId} with {Count} items (user: {UserId})",
            newPlaylistId, watchlistId, jellyfinItemIds.Count, jellyfinUserId);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UnlinkPlaylistAsync(int watchlistId)
    {
        var watchlist = await _context.Watchlists.FindAsync(watchlistId);
        if (watchlist is null)
            return ServiceResult.Fail("Watchlist not found", 404);

        if (watchlist.JellyfinPlaylistId is null)
            return ServiceResult.Fail("Watchlist is not linked to a Jellyfin playlist", 400);

        var deleted = await _jellyfinClient.DeletePlaylistAsync(watchlist.JellyfinPlaylistId);
        if (!deleted)
            _logger.LogWarning("Failed to delete Jellyfin playlist {PlaylistId}, unlinking anyway", watchlist.JellyfinPlaylistId);

        watchlist.JellyfinPlaylistId = null;
        watchlist.JellyfinPlaylistUserId = null;
        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    private async Task<List<string>> ResolveJellyfinItemIdsAsync(IEnumerable<Domain.Entities.WatchlistItem> items, string? targetJellyfinUserId = null)
    {
        var mediaItemIds = items
            .Where(i => i.MediaItemId.HasValue)
            .Select(i => i.MediaItemId!.Value)
            .ToList();

        var jellyfinMap = await BuildJellyfinMapAsync(mediaItemIds, targetJellyfinUserId);

        var resolved = items
            .Where(i => i.MediaItemId.HasValue && jellyfinMap.ContainsKey(i.MediaItemId.Value))
            .Select(i => jellyfinMap[i.MediaItemId!.Value])
            .ToList();

        var skippedIds = mediaItemIds.Except(jellyfinMap.Keys).ToList();
        if (skippedIds.Count > 0)
            _logger.LogWarning("Could not resolve {Count} media items to Jellyfin IDs: [{Ids}]", skippedIds.Count, string.Join(", ", skippedIds));

        _logger.LogInformation("Resolved {Resolved}/{Total} items. Jellyfin IDs: [{Ids}]",
            resolved.Count, mediaItemIds.Count, string.Join(", ", resolved));

        return resolved;
    }

    private Task<Dictionary<int, string>> BuildJellyfinMapAsync(IEnumerable<Domain.Entities.WatchlistItem> items, string? targetJellyfinUserId = null)
    {
        var mediaItemIds = items
            .Where(i => i.MediaItemId.HasValue)
            .Select(i => i.MediaItemId!.Value)
            .ToList();
        return BuildJellyfinMapAsync(mediaItemIds, targetJellyfinUserId);
    }

    /// <summary>
    /// Builds a map of MediaItemId → JellyfinItemId.
    /// Step 1: Direct link via jellyfin_library_item.media_item_id.
    /// Step 2: Match by provider IDs in the DB.
    /// Step 3: Query Jellyfin API directly as final fallback.
    /// </summary>
    private async Task<Dictionary<int, string>> BuildJellyfinMapAsync(List<int> mediaItemIds, string? targetJellyfinUserId = null)
    {
        if (mediaItemIds.Count == 0)
            return new Dictionary<int, string>();

        // Step 1: Direct media_item_id link
        var jellyfinMap = await _context.JellyfinLibraryItems
            .Where(j => j.MediaItemId.HasValue && mediaItemIds.Contains(j.MediaItemId.Value))
            .GroupBy(j => j.MediaItemId!.Value)
            .ToDictionaryAsync(g => g.Key, g => g.First().JellyfinItemId);

        // Remove synthetic (non-GUID) entries from the map so they get re-resolved
        var syntheticEntries = jellyfinMap.Where(kv => !Guid.TryParse(kv.Value, out _)).ToList();
        foreach (var entry in syntheticEntries)
            jellyfinMap.Remove(entry.Key);

        var unresolvedIds = mediaItemIds.Except(jellyfinMap.Keys).ToList();
        if (unresolvedIds.Count == 0)
            return jellyfinMap;

        // Load provider IDs for unresolved items (used in step 2 and 3)
        var unresolvedItems = await _context.Set<Domain.Entities.MediaItem>()
            .Where(m => unresolvedIds.Contains(m.Id))
            .Select(m => new { m.Id, m.TmdbId, m.ImdbId, m.MediaType })
            .ToListAsync();

        // Step 2: Match by provider IDs in the DB
        foreach (var item in unresolvedItems)
        {
            if (jellyfinMap.ContainsKey(item.Id)) continue;

            string? jellyfinItemId = null;

            if (item.TmdbId.HasValue)
            {
                jellyfinItemId = await _context.JellyfinLibraryItems
                    .Where(j => j.MediaItemId.HasValue && j.MediaItem != null
                        && j.MediaItem.TmdbId == item.TmdbId
                        && j.MediaItem.MediaType == item.MediaType)
                    .Select(j => j.JellyfinItemId)
                    .FirstOrDefaultAsync();
            }

            if (jellyfinItemId is null && !string.IsNullOrEmpty(item.ImdbId))
            {
                jellyfinItemId = await _context.JellyfinLibraryItems
                    .Where(j => j.MediaItemId.HasValue && j.MediaItem != null
                        && j.MediaItem.ImdbId == item.ImdbId)
                    .Select(j => j.JellyfinItemId)
                    .FirstOrDefaultAsync();
            }

            if (jellyfinItemId is not null)
            {
                jellyfinMap[item.Id] = jellyfinItemId;
                _logger.LogDebug("Resolved MediaItem {MediaItemId} via DB provider ID fallback", item.Id);
            }
        }

        // Step 3: Query Jellyfin API directly for remaining unresolved items
        unresolvedIds = unresolvedItems
            .Where(i => !jellyfinMap.ContainsKey(i.Id))
            .Select(i => i.Id)
            .ToList();

        if (unresolvedIds.Count > 0)
        {
            try
            {
                var jellyfinUserId = targetJellyfinUserId ?? await GetAnyJellyfinUserIdAsync();
                if (jellyfinUserId is not null)
                {
                    var allJellyfinItems = await _jellyfinClient.GetItemsAsync(
                        jellyfinUserId, itemTypes: "Movie,Series");

                    // Build provider ID → Jellyfin item ID maps
                    var tmdbMap = new Dictionary<(int tmdbId, string type), string>();
                    var imdbMap = new Dictionary<string, string>();

                    foreach (var jfItem in allJellyfinItems)
                    {
                        if (jfItem.ProviderIds is null) continue;
                        var jfType = jfItem.Type == "Movie" ? "Movie" : "Series";

                        if (jfItem.ProviderIds.TryGetValue("Tmdb", out var tmdbStr) && int.TryParse(tmdbStr, out var tmdbId))
                            tmdbMap.TryAdd((tmdbId, jfType), jfItem.Id);

                        if (jfItem.ProviderIds.TryGetValue("Imdb", out var imdbId) && !string.IsNullOrEmpty(imdbId))
                            imdbMap.TryAdd(imdbId, jfItem.Id);
                    }

                    // Match unresolved items
                    foreach (var item in unresolvedItems.Where(i => !jellyfinMap.ContainsKey(i.Id)))
                    {
                        string? resolved = null;
                        var mediaTypeStr = item.MediaType == MediaType.Movie ? "Movie" : "Series";

                        if (item.TmdbId.HasValue && tmdbMap.TryGetValue((item.TmdbId.Value, mediaTypeStr), out var byTmdb))
                            resolved = byTmdb;
                        else if (!string.IsNullOrEmpty(item.ImdbId) && imdbMap.TryGetValue(item.ImdbId, out var byImdb))
                            resolved = byImdb;

                        if (resolved is not null)
                        {
                            jellyfinMap[item.Id] = resolved;
                            _logger.LogDebug("Resolved MediaItem {MediaItemId} via Jellyfin API lookup", item.Id);
                        }
                    }

                    // Auto-repair synthetic DB records with the real Jellyfin IDs
                    if (syntheticEntries.Count > 0)
                    {
                        var repairedMediaItemIds = syntheticEntries
                            .Where(e => jellyfinMap.ContainsKey(e.Key))
                            .Select(e => e.Key)
                            .ToList();

                        if (repairedMediaItemIds.Count > 0)
                        {
                            var dbRecords = await _context.JellyfinLibraryItems
                                .Where(j => j.MediaItemId.HasValue && repairedMediaItemIds.Contains(j.MediaItemId.Value))
                                .ToListAsync();

                            foreach (var record in dbRecords)
                            {
                                if (record.MediaItemId.HasValue && jellyfinMap.TryGetValue(record.MediaItemId.Value, out var realId)
                                    && !Guid.TryParse(record.JellyfinItemId, out _))
                                {
                                    _logger.LogInformation("Auto-repaired Jellyfin ID for MediaItem {MediaItemId}: '{OldId}' → '{NewId}'",
                                        record.MediaItemId, record.JellyfinItemId, realId);
                                    record.JellyfinItemId = realId;
                                }
                            }

                            await _context.SaveChangesAsync();
                        }

                        // Delete synthetic records that couldn't be resolved
                        var unrepairedMediaItemIds = syntheticEntries
                            .Where(e => !jellyfinMap.ContainsKey(e.Key))
                            .Select(e => e.Key)
                            .ToList();

                        if (unrepairedMediaItemIds.Count > 0)
                        {
                            var orphanRecords = await _context.JellyfinLibraryItems
                                .Where(j => j.MediaItemId.HasValue && unrepairedMediaItemIds.Contains(j.MediaItemId.Value)
                                    && !string.IsNullOrEmpty(j.JellyfinItemId))
                                .ToListAsync();

                            var toDelete = orphanRecords.Where(r => !Guid.TryParse(r.JellyfinItemId, out _)).ToList();
                            if (toDelete.Count > 0)
                            {
                                _logger.LogWarning("Deleting {Count} unresolvable synthetic Jellyfin records for MediaItems: [{Ids}]",
                                    toDelete.Count, string.Join(", ", toDelete.Select(r => r.MediaItemId)));
                                _context.JellyfinLibraryItems.RemoveRange(toDelete);
                                await _context.SaveChangesAsync();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Jellyfin API fallback failed, some items may show as not in library");
            }
        }

        return jellyfinMap;
    }

    private async Task<string?> GetAnyJellyfinUserIdAsync()
    {
        return await _context.Profiles
            .Where(p => p.JellyfinUserId != null && p.JellyfinUserId != "")
            .Select(p => p.JellyfinUserId)
            .FirstOrDefaultAsync();
    }
}
