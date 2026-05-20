namespace Jellywatch.Api.Domain;

public class UserWatchlistPreference
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? DefaultWatchlistId { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual User User { get; set; } = null!;
    public virtual Watchlist? DefaultWatchlist { get; set; }
}
