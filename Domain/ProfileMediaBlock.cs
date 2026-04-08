namespace Jellywatch.Api.Domain;

public class ProfileMediaBlock
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int MediaItemId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Profile Profile { get; set; } = null!;
    public MediaItem MediaItem { get; set; } = null!;
}
