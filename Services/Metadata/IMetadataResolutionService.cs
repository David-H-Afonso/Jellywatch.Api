using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Services.Metadata;

public interface IMetadataResolutionService
{
    Task<MediaItem?> ResolveSeriesAsync(string jellyfinItemId, string name, int? year = null, int? tmdbId = null, string? imdbId = null);
    Task<MediaItem?> ResolveMovieAsync(string jellyfinItemId, string name, int? year = null, int? tmdbId = null, string? imdbId = null);
    Task PopulateSeasonsAndEpisodesAsync(int seriesId);
    Task RefreshRatingsAsync(int mediaItemId);
    Task RefreshTranslationsAsync(int mediaItemId);
    Task RefreshImagesAsync(int mediaItemId);
    Task RefreshMediaItemAsync(int mediaItemId, int? forceTmdbId = null, bool refreshImages = true);
    Task<List<PosterOptionDto>> GetPosterOptionsAsync(int mediaItemId);
    Task SelectPosterAsync(int mediaItemId, string remoteUrl);
    Task<List<PosterOptionDto>> GetLogoOptionsAsync(int mediaItemId);
    Task SelectLogoAsync(int mediaItemId, string remoteUrl);
    Task<int> RefreshAllMetadataAsync();
    Task<int> RefreshAllImagesAsync();
}
