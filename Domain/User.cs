namespace Jellywatch.Api.Domain;

public class User
{
    public int Id { get; set; }
    public string JellyfinUserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string? AvatarUrl { get; set; }
    public string? JellyfinServerUrl { get; set; }
    public string PreferredLanguage { get; set; } = "en";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual ICollection<Profile> Profiles { get; set; } = new List<Profile>();
}
