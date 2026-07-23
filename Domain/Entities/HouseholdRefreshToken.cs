namespace Jellywatch.Api.Domain.Entities;

public sealed class HouseholdRefreshToken
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConnectionId { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;
    public string FamilyId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenId { get; set; }

    public HouseholdConnection Connection { get; set; } = null!;
    public HouseholdRefreshToken? ReplacedByToken { get; set; }
}
