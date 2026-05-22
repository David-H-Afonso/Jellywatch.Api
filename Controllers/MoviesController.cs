using Microsoft.AspNetCore.Mvc;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Common;

namespace Jellywatch.Api.Controllers;

[Route("api/media/movies")]
public class MoviesController : BaseApiController
{
    private readonly IMediaQueryService _mediaQueryService;

    public MoviesController(IMediaQueryService mediaQueryService)
    {
        _mediaQueryService = mediaQueryService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<MovieListDto>>> GetMovies([FromQuery] MediaQueryParameters query)
    {
        var result = await _mediaQueryService.GetMoviesAsync(query);
        return ToActionResult(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MovieDetailDto>> GetMovieDetail(int id, [FromQuery] int? profileId)
    {
        var result = await _mediaQueryService.GetMovieDetailAsync(id, profileId);
        return ToActionResult(result);
    }

    [HttpPatch("{id:int}/rating")]
    public async Task<IActionResult> RateMovie(int id, [FromQuery] int profileId, [FromBody] UserRatingDto dto)
    {
        var result = await _mediaQueryService.RateMovieAsync(id, profileId, dto);
        return ToActionResult(result);
    }

    [HttpGet("{id:int}/credits")]
    public async Task<ActionResult<List<CastMemberDto>>> GetMovieCredits(int id)
    {
        var result = await _mediaQueryService.GetMovieCreditsAsync(id);
        return ToActionResult(result);
    }
}
