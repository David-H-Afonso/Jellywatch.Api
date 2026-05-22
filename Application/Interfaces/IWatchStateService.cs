using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Application.Interfaces;

public interface IWatchStateService
{
    Task<(bool Success, string Message, string? Error)> UpdateEpisodeStateAsync(int profileId, int episodeId, WatchStateUpdateDto dto);
    Task<(bool Success, string Message, string? Error)> UpdateMovieStateAsync(int profileId, int movieId, WatchStateUpdateDto dto);
    Task<(bool Success, string Message, string? Error)> UpdateSeasonStateAsync(int profileId, int seasonId, WatchStateUpdateDto dto);
    Task<(bool Success, string Message, string? Error)> UpdateSeriesStateAsync(int profileId, int seriesId, WatchStateUpdateDto dto);
}
