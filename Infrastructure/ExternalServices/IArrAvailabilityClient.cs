namespace Jellywatch.Api.Infrastructure.ExternalServices;

public interface IArrAvailabilityClient
{
    bool IsConfigured { get; }
    Task<ArrMediaStatus?> GetMovieStatusAsync(int tmdbId);
    Task<ArrMediaStatus?> GetSeriesStatusAsync(int tvdbId);
}

public record ArrMediaStatus(
    bool IsMonitored,
    bool HasFile,
    string? Title,
    string? Status,
    string? QualityProfileName,
    int? SizeOnDisk,
    int? EpisodeFileCount,
    int? TotalEpisodeCount
);
