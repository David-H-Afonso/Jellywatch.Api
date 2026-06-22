using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Domain.Entities;

public class Watchlist
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverImagePath { get; set; }
    public string? JellyfinPlaylistId { get; set; }
    public string? JellyfinPlaylistUserId { get; set; }
    public int OwnerUserId { get; set; }
    public WatchlistState State { get; set; } = WatchlistState.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual User OwnerUser { get; set; } = null!;
    public virtual ICollection<WatchlistMember> Members { get; set; } = new List<WatchlistMember>();
    public virtual ICollection<WatchlistItem> Items { get; set; } = new List<WatchlistItem>();
    public virtual ICollection<WatchlistInvitation> Invitations { get; set; } = new List<WatchlistInvitation>();
    public virtual ICollection<WatchlistAccessRequest> AccessRequests { get; set; } = new List<WatchlistAccessRequest>();
}
