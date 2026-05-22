using Jellywatch.Api.Contracts.External;

namespace Jellywatch.Api.Infrastructure.ExternalServices;

public interface ITvMazeApiClient
{
    Task<List<TvMazeSearchResult>> SearchShowsAsync(string query);
    Task<TvMazeShow?> GetShowAsync(int tvMazeId);
    Task<TvMazeShow?> LookupByImdbAsync(string imdbId);
    Task<TvMazeShow?> LookupByTvdbAsync(int tvdbId);
    Task<List<TvMazeEpisode>> GetEpisodesAsync(int tvMazeId);
}
