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

        var jellyfinItemIds = await ResolveJellyfinItemIdsAsync(watchlist.Items);

        var playlistId = await _jellyfinClient.CreatePlaylistAsync(
            watchlist.Name,
            jellyfinItemIds,
            jellyfinUserId);

        if (playlistId is null)
            return ServiceResult.Fail("Failed to create playlist in Jellyfin", 502);

        watchlist.JellyfinPlaylistId = playlistId;
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

        var jellyfinItemIds = await ResolveJellyfinItemIdsAsync(watchlist.Items);

        var success = await _jellyfinClient.UpdatePlaylistItemsAsync(watchlist.JellyfinPlaylistId, jellyfinItemIds);
        if (!success)
            return ServiceResult.Fail("Failed to sync playlist with Jellyfin", 502);

        _logger.LogInformation("Synced Jellyfin playlist {PlaylistId} with {Count} items", watchlist.JellyfinPlaylistId, jellyfinItemIds.Count);
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
        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    private async Task<List<string>> ResolveJellyfinItemIdsAsync(IEnumerable<Domain.Entities.WatchlistItem> items)
    {
        var mediaItemIds = items
            .Where(i => i.MediaItemId.HasValue)
            .Select(i => i.MediaItemId!.Value)
            .ToList();

        var jellyfinMap = await BuildJellyfinMapAsync(mediaItemIds);

        return items
            .Where(i => i.MediaItemId.HasValue && jellyfinMap.ContainsKey(i.MediaItemId.Value))
            .Select(i => jellyfinMap[i.MediaItemId!.Value])
            .ToList();
    }

    private Task<Dictionary<int, string>> BuildJellyfinMapAsync(IEnumerable<Domain.Entities.WatchlistItem> items)
    {
        var mediaItemIds = items
            .Where(i => i.MediaItemId.HasValue)
            .Select(i => i.MediaItemId!.Value)
            .ToList();
        return BuildJellyfinMapAsync(mediaItemIds);
    }

    /// <summary>
    /// Builds a map of MediaItemId → JellyfinItemId.
    /// First tries direct link via jellyfin_library_item.media_item_id,
    /// then falls back to matching by provider IDs (TmdbId, ImdbId).
    /// </summary>
    private async Task<Dictionary<int, string>> BuildJellyfinMapAsync(List<int> mediaItemIds)
    {
        if (mediaItemIds.Count == 0)
            return new Dictionary<int, string>();

        // Step 1: Direct media_item_id link
        var jellyfinMap = await _context.JellyfinLibraryItems
            .Where(j => j.MediaItemId.HasValue && mediaItemIds.Contains(j.MediaItemId.Value))
            .GroupBy(j => j.MediaItemId!.Value)
            .ToDictionaryAsync(g => g.Key, g => g.First().JellyfinItemId);

        // Step 2: For unresolved items, try matching via provider IDs
        var unresolvedIds = mediaItemIds.Except(jellyfinMap.Keys).ToList();
        if (unresolvedIds.Count == 0)
            return jellyfinMap;

        var unresolvedItems = await _context.Set<Domain.Entities.MediaItem>()
            .Where(m => unresolvedIds.Contains(m.Id))
            .Select(m => new { m.Id, m.TmdbId, m.ImdbId, m.MediaType })
            .ToListAsync();

        foreach (var item in unresolvedItems)
        {
            if (jellyfinMap.ContainsKey(item.Id)) continue;

            // Try TMDB ID match: find a JellyfinLibraryItem whose linked MediaItem has the same TmdbId
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

            // Try IMDB ID match as fallback
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
                _logger.LogDebug("Resolved MediaItem {MediaItemId} to Jellyfin item {JellyfinItemId} via provider ID fallback", item.Id, jellyfinItemId);
            }
        }

        return jellyfinMap;
    }
}
