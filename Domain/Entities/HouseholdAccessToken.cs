namespace Jellywatch.Api.Domain.Entities;

public sealed class HouseholdAccessToken
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConnectionId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public HouseholdConnection Connection { get; set; } = null!;
}
