namespace Jellywatch.Api.Domain.Entities;

public class BlacklistedItem
{
    public int Id { get; set; }
    public string JellyfinItemId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}
