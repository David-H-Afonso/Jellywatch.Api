using Jellywatch.Api.Contracts;
using Jellywatch.Api.Common;

namespace Jellywatch.Api.Application.Interfaces;

public interface IMediaQueryService
{
    // Series
    Task<ServiceResult<PagedResult<SeriesListDto>>> GetSeriesAsync(MediaQueryParameters query);
    Task<ServiceResult<SeriesDetailDto>> GetSeriesDetailAsync(int id, int? profileId, int? currentUserId);
    Task<ServiceResult<List<SeasonDto>>> GetSeasonsAsync(int seriesId, int? profileId);
    Task<ServiceResult<List<EpisodeDto>>> GetEpisodesAsync(int seasonId, int? profileId);
    Task<ServiceResult<object>> RateSeriesAsync(int id, int profileId, UserRatingDto dto);
    Task<ServiceResult<object>> RateEpisodeAsync(int seriesId, int episodeId, int profileId, UserRatingDto dto);
    Task<ServiceResult<object>> RateSeasonAsync(int seriesId, int seasonId, int profileId, UserRatingDto dto);
    Task<ServiceResult<List<CastMemberDto>>> GetSeriesCreditsAsync(int id);

    // Movies
    Task<ServiceResult<PagedResult<MovieListDto>>> GetMoviesAsync(MediaQueryParameters query);
    Task<ServiceResult<MovieDetailDto>> GetMovieDetailAsync(int id, int? profileId);
    Task<ServiceResult<object>> RateMovieAsync(int id, int profileId, UserRatingDto dto);
    Task<ServiceResult<List<CastMemberDto>>> GetMovieCreditsAsync(int id);
}
