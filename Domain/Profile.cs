namespace Jellywatch.Api.Domain;

public class Profile
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string JellyfinUserId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsJoint { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual User? User { get; set; }
    public virtual ICollection<PropagationRule> SourceRules { get; set; } = new List<PropagationRule>();
    public virtual ICollection<PropagationRule> TargetRules { get; set; } = new List<PropagationRule>();
    public virtual ICollection<WatchEvent> WatchEvents { get; set; } = new List<WatchEvent>();
    public virtual ICollection<ProfileWatchState> WatchStates { get; set; } = new List<ProfileWatchState>();
    public virtual ICollection<ProfileNote> Notes { get; set; } = new List<ProfileNote>();
}
