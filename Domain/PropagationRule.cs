namespace Jellywatch.Api.Domain;

public class PropagationRule
{
    public int Id { get; set; }
    public int SourceProfileId { get; set; }
    public int TargetProfileId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }

    public virtual Profile SourceProfile { get; set; } = null!;
    public virtual Profile TargetProfile { get; set; } = null!;
}
