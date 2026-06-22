namespace Jellywatch.Api.Application.Interfaces;

public interface IJellyfinPlaylistSyncService
{
    Task<ServiceResult<PlaylistSyncPreviewDto>> PreviewSyncAsync(int watchlistId);
    Task<ServiceResult> CreatePlaylistFromWatchlistAsync(int watchlistId, string jellyfinUserId);
    Task<ServiceResult> SyncPlaylistAsync(int watchlistId);
    Task<ServiceResult> UnlinkPlaylistAsync(int watchlistId);
}

public record PlaylistSyncPreviewDto(
    List<PlaylistSyncItemDto> SyncableItems,
    List<PlaylistSkippedItemDto> SkippedItems,
    int TotalWatchlistItems
);

public record PlaylistSyncItemDto(string Title, string MediaType, int Position, string JellyfinItemId);
public record PlaylistSkippedItemDto(string Title, string MediaType, int OriginalPosition, string Reason);
