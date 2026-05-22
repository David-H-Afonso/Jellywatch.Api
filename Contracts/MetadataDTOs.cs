namespace Jellywatch.Api.Contracts;

public class ImportQueueItemDto
{
    public int Id { get; set; }
    public string JellyfinItemId { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string Status { get; set; } = string.Empty;
    public int RetryCount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class MetadataRefreshDto
{
    public int MediaItemId { get; set; }
    public bool ForceRefresh { get; set; }
}

public class MediaLibraryItemDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string MediaType { get; set; } = string.Empty;
    public string? PosterPath { get; set; }
    public string? ReleaseDate { get; set; }
    public string? Status { get; set; }
    public int? TmdbId { get; set; }
    public int? TvMazeId { get; set; }
    public string? ImdbId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class BlacklistedItemDto
{
    public int Id { get; set; }
    public string JellyfinItemId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AddToBlacklistDto
{
    public string JellyfinItemId { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Reason { get; set; }
}

public class RefreshMediaItemDto
{
    public int? ForceTmdbId { get; set; }
    public bool RefreshImages { get; set; } = true;
}

public class PosterOptionDto
{
    public int Id { get; set; }
    public string RemoteUrl { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Language { get; set; }
}

public class SelectPosterDto
{
    public string RemoteUrl { get; set; } = string.Empty;
}
