using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Domain;

public class WatchlistMember
{
    public int Id { get; set; }
    public int WatchlistId { get; set; }
    public int UserId { get; set; }
    public WatchlistRole Role { get; set; } = WatchlistRole.Member;
    public int? InvitedByUserId { get; set; }
    public bool CanAddItems { get; set; } = true;
    public bool CanRemoveItems { get; set; }
    public bool CanReorderItems { get; set; } = true;
    public bool CanUpdateItemStatus { get; set; } = true;
    public bool CanInviteMembers { get; set; }
    public bool CanManageMembers { get; set; }
    public bool CanUpdateWatchlist { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual Watchlist Watchlist { get; set; } = null!;
    public virtual User User { get; set; } = null!;
    public virtual User? InvitedByUser { get; set; }
}
