namespace Jellywatch.Api.Contracts;

public class NoteDto
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public int? SeasonId { get; set; }
    public int? EpisodeId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class NoteCreateUpdateDto
{
    public int MediaItemId { get; set; }
    public int? SeasonId { get; set; }
    public int? EpisodeId { get; set; }
    public string Text { get; set; } = string.Empty;
}
