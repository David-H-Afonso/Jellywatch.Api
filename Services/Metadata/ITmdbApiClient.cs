using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Services.Metadata;

public interface ITmdbApiClient
{
    Task<List<TmdbTvSearchResult>> SearchTvAsync(string query, int? year = null);
    Task<List<TmdbMovieSearchResult>> SearchMovieAsync(string query, int? year = null);
    Task<TmdbTvDetails?> GetTvDetailsAsync(int tmdbId);
    Task<TmdbSeasonDetails?> GetTvSeasonAsync(int tmdbId, int seasonNumber);
    Task<TmdbMovieDetails?> GetMovieDetailsAsync(int tmdbId);
    Task<TmdbImageCollection?> GetImagesAsync(int tmdbId, string mediaType, bool forceRefresh = false);
    Task<TmdbTranslationsResponse?> GetTranslationsAsync(int tmdbId, string mediaType);
    Task<TmdbAggregateCreditsResponse?> GetTvAggregateCreditsAsync(int tmdbId);
    Task<TmdbCreditsResponse?> GetMovieCreditsAsync(int tmdbId);
    Task<TmdbPersonCreditsResponse?> GetPersonCombinedCreditsAsync(int personId);
    Task<TmdbPersonDetails?> GetPersonDetailsAsync(int personId);
    bool IsConfigured { get; }
}
