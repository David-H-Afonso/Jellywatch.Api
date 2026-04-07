using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Services.Metadata;

public interface ITvMazeApiClient
{
    Task<List<TvMazeSearchResult>> SearchShowsAsync(string query);
    Task<TvMazeShow?> GetShowAsync(int tvMazeId);
    Task<List<TvMazeEpisode>> GetEpisodesAsync(int tvMazeId);
}
