using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Domain;

public class WatchlistItem
{
    public int Id { get; set; }
    public int WatchlistId { get; set; }
    public WatchlistItemType ItemType { get; set; } = WatchlistItemType.MediaItem;
    public int? MediaItemId { get; set; }
    public int? ChildWatchlistId { get; set; }
    public WatchlistStatus Status { get; set; } = WatchlistStatus.WantToWatch;
    public int Position { get; set; }
    public int? AddedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual Watchlist Watchlist { get; set; } = null!;
    public virtual MediaItem? MediaItem { get; set; }
    public virtual Watchlist? ChildWatchlist { get; set; }
    public virtual User? AddedByUser { get; set; }
}
