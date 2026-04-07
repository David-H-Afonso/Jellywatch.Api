namespace Jellywatch.Api.Domain;

public class MediaTranslation
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public string Language { get; set; } = string.Empty;
    public string? Title { get; set; }
    public string? Overview { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual MediaItem MediaItem { get; set; } = null!;
}
