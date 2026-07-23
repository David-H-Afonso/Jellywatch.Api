namespace Jellywatch.Api.Domain.Entities;

public sealed class HouseholdAuthorizationCode
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ConnectionId { get; set; } = string.Empty;
    public string CodeHash { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }

    public HouseholdConnection Connection { get; set; } = null!;
}
