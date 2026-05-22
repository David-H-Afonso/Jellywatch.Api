using Microsoft.AspNetCore.Mvc;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Common;

namespace Jellywatch.Api.Controllers;

[Route("api/media/series")]
public class SeriesController : BaseApiController
{
    private readonly IMediaQueryService _mediaQueryService;

    public SeriesController(IMediaQueryService mediaQueryService)
    {
        _mediaQueryService = mediaQueryService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<SeriesListDto>>> GetSeries([FromQuery] MediaQueryParameters query)
    {
        var result = await _mediaQueryService.GetSeriesAsync(query);
        return ToActionResult(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SeriesDetailDto>> GetSeriesDetail(int id, [FromQuery] int? profileId)
    {
        var result = await _mediaQueryService.GetSeriesDetailAsync(id, profileId, CurrentUserId);
        return ToActionResult(result);
    }

    [HttpGet("{id:int}/seasons")]
    public async Task<ActionResult<List<SeasonDto>>> GetSeasons(int id, [FromQuery] int? profileId)
    {
        var result = await _mediaQueryService.GetSeasonsAsync(id, profileId);
        return ToActionResult(result);
    }

    [HttpGet("seasons/{seasonId:int}/episodes")]
    public async Task<ActionResult<List<EpisodeDto>>> GetEpisodes(int seasonId, [FromQuery] int? profileId)
    {
        var result = await _mediaQueryService.GetEpisodesAsync(seasonId, profileId);
        return ToActionResult(result);
    }

    [HttpPatch("{id:int}/rating")]
    public async Task<IActionResult> RateSeries(int id, [FromQuery] int profileId, [FromBody] UserRatingDto dto)
    {
        var result = await _mediaQueryService.RateSeriesAsync(id, profileId, dto);
        return ToActionResult(result);
    }

    [HttpPatch("{id:int}/episodes/{episodeId:int}/rating")]
    public async Task<IActionResult> RateEpisode(int id, int episodeId, [FromQuery] int profileId, [FromBody] UserRatingDto dto)
    {
        var result = await _mediaQueryService.RateEpisodeAsync(id, episodeId, profileId, dto);
        return ToActionResult(result);
    }

    [HttpPatch("{id:int}/seasons/{seasonId:int}/rating")]
    public async Task<IActionResult> RateSeason(int id, int seasonId, [FromQuery] int profileId, [FromBody] UserRatingDto dto)
    {
        var result = await _mediaQueryService.RateSeasonAsync(id, seasonId, profileId, dto);
        return ToActionResult(result);
    }

    [HttpGet("{id:int}/credits")]
    public async Task<ActionResult<List<CastMemberDto>>> GetSeriesCredits(int id)
    {
        var result = await _mediaQueryService.GetSeriesCreditsAsync(id);
        return ToActionResult(result);
    }
}
