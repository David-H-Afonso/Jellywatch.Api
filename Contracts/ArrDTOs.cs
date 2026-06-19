namespace Jellywatch.Api.Contracts;

public class MediaAvailabilityDto
{
    public string Source { get; set; } = string.Empty;
    public bool IsAvailable { get; set; }
    public bool IsMonitored { get; set; }
    public string? Status { get; set; }
    public int? SizeMb { get; set; }
    public int? EpisodeFileCount { get; set; }
    public int? TotalEpisodeCount { get; set; }
}
