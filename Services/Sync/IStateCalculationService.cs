using Jellywatch.Api.Domain;
using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Services.Sync;

public interface IStateCalculationService
{
    WatchState CalculateEpisodeState(bool played, long positionTicks, long? totalTicks);
    WatchState CalculateMovieState(bool played, long positionTicks, long? totalTicks);
    WatchState CalculateSeriesState(IEnumerable<WatchState> episodeStates);
    WatchState CalculateSeasonState(IEnumerable<WatchState> episodeStates);
    Task RecalculateProfileWatchStateAsync(int profileId, int mediaItemId, int? episodeId = null, int? movieId = null);
}
