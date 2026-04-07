namespace Jellywatch.Api.Domain;

public class Series
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public int? TotalSeasons { get; set; }
    public int? TotalEpisodes { get; set; }
    public string? Network { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual MediaItem MediaItem { get; set; } = null!;
    public virtual ICollection<Season> Seasons { get; set; } = new List<Season>();
}
