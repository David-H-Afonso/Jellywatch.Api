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

    public async Task<ServiceResult<PlaylistSyncPreviewDto>> PreviewSyncAsync(int watchlistId)
    {
        var watchlist = await _context.Watchlists
            .Include(w => w.Items.OrderBy(i => i.Position))
                .ThenInclude(i => i.MediaItem)
            .FirstOrDefaultAsync(w => w.Id == watchlistId);

        if (watchlist is null)
            return ServiceResult<PlaylistSyncPreviewDto>.Fail("Watchlist not found", 404);

        var mediaItemIds = watchlist.Items
            .Where(i => i.MediaItemId.HasValue)
            .Select(i => i.MediaItemId!.Value)
            .ToList();

        var jellyfinMap = await _context.JellyfinLibraryItems
            .Where(j => j.MediaItemId.HasValue && mediaItemIds.Contains(j.MediaItemId.Value))
            .ToDictionaryAsync(j => j.MediaItemId!.Value, j => j.JellyfinItemId);

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

        return ServiceResult<PlaylistSyncPreviewDto>.Ok(new PlaylistSyncPreviewDto(syncable, skipped, watchlist.Items.Count));
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

        var jellyfinMap = await _context.JellyfinLibraryItems
            .Where(j => j.MediaItemId.HasValue && mediaItemIds.Contains(j.MediaItemId.Value))
            .ToDictionaryAsync(j => j.MediaItemId!.Value, j => j.JellyfinItemId);

        return items
            .Where(i => i.MediaItemId.HasValue && jellyfinMap.ContainsKey(i.MediaItemId.Value))
            .Select(i => jellyfinMap[i.MediaItemId!.Value])
            .ToList();
    }
}
