using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Domain;

public class MediaImage
{
    public int Id { get; set; }
    public int MediaItemId { get; set; }
    public int? SeasonId { get; set; }
    public int? EpisodeId { get; set; }
    public ImageType ImageType { get; set; }
    public string? RemoteUrl { get; set; }
    public string? LocalPath { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Language { get; set; }
    public DateTime CreatedAt { get; set; }

    public virtual MediaItem MediaItem { get; set; } = null!;
    public virtual Season? Season { get; set; }
    public virtual Episode? Episode { get; set; }
}
