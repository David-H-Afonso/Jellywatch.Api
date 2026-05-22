using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Domain.Entities;

public class WatchlistAccessRequest
{
    public int Id { get; set; }
    public int WatchlistId { get; set; }
    public int RequestingUserId { get; set; }
    public WatchlistAccessRequestStatus Status { get; set; } = WatchlistAccessRequestStatus.Pending;
    public string? Message { get; set; }
    public int? RespondedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RespondedAt { get; set; }

    public virtual Watchlist Watchlist { get; set; } = null!;
    public virtual User RequestingUser { get; set; } = null!;
    public virtual User? RespondedByUser { get; set; }
}
