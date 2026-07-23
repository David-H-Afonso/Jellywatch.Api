namespace Jellywatch.Api.Domain.Entities;

public sealed class HouseholdConnection
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int UserId { get; set; }
    public int ProfileId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string GrantedScopes { get; set; } = string.Empty;
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public User User { get; set; } = null!;
    public Profile Profile { get; set; } = null!;
    public ICollection<HouseholdAuthorizationCode> AuthorizationCodes { get; set; } = new List<HouseholdAuthorizationCode>();
    public ICollection<HouseholdAccessToken> AccessTokens { get; set; } = new List<HouseholdAccessToken>();
    public ICollection<HouseholdRefreshToken> RefreshTokens { get; set; } = new List<HouseholdRefreshToken>();
}
