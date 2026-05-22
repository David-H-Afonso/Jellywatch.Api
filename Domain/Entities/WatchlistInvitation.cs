using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Domain.Entities;

public class WatchlistInvitation
{
    public int Id { get; set; }
    public int WatchlistId { get; set; }
    public int InvitedUserId { get; set; }
    public int InvitedByUserId { get; set; }
    public WatchlistInvitationStatus Status { get; set; } = WatchlistInvitationStatus.Pending;
    public WatchlistRole Role { get; set; } = WatchlistRole.Member;
    public bool CanAddItems { get; set; } = true;
    public bool CanRemoveItems { get; set; }
    public bool CanReorderItems { get; set; } = true;
    public bool CanUpdateItemStatus { get; set; } = true;
    public bool CanInviteMembers { get; set; }
    public bool CanManageMembers { get; set; }
    public bool CanUpdateWatchlist { get; set; }
    public string? Message { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }

    public virtual Watchlist Watchlist { get; set; } = null!;
    public virtual User InvitedUser { get; set; } = null!;
    public virtual User InvitedByUser { get; set; } = null!;
}
