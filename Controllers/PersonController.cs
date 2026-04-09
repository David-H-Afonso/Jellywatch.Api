using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure;
using Jellywatch.Api.Services.Metadata;

namespace Jellywatch.Api.Controllers;

[Route("api/media/person")]
public class PersonController : BaseApiController
{
    private readonly JellywatchDbContext _context;
    private readonly ITmdbApiClient _tmdbClient;

    public PersonController(JellywatchDbContext context, ITmdbApiClient tmdbClient)
    {
        _context = context;
        _tmdbClient = tmdbClient;
    }

    [HttpGet("{tmdbPersonId:int}/credits")]
    public async Task<ActionResult<PersonCreditsDto>> GetPersonCredits(int tmdbPersonId)
    {
        if (!_tmdbClient.IsConfigured)
            return Ok(new PersonCreditsDto { TmdbPersonId = tmdbPersonId });

        var personDetails = await _tmdbClient.GetPersonDetailsAsync(tmdbPersonId);
        var tmdbCredits = await _tmdbClient.GetPersonCombinedCreditsAsync(tmdbPersonId);
        if (tmdbCredits is null)
            return NotFound(new { message = "Person not found" });

        var tmdbIds = tmdbCredits.Cast?
            .Where(c => c.Id > 0)
            .Select(c => c.Id)
            .Distinct()
            .ToList() ?? [];

        var localMediaMap = await _context.MediaItems
            .Where(mi => mi.TmdbId.HasValue && tmdbIds.Contains(mi.TmdbId.Value))
            .Select(mi => new { mi.TmdbId, mi.MediaType, mi.Id })
            .ToDictionaryAsync(mi => mi.TmdbId!.Value, mi => new { mi.Id, mi.MediaType });

        var credits = tmdbCredits.Cast?
            .OrderByDescending(c => c.VoteAverage ?? 0)
            .ThenByDescending(c => c.ReleaseDate ?? c.FirstAirDate ?? "")
            .Select(c => new PersonCreditItemDto
            {
                LocalMediaItemId = localMediaMap.ContainsKey(c.Id)
                    ? (c.MediaType == "tv" && localMediaMap[c.Id].MediaType == MediaType.Series
                        ? localMediaMap[c.Id].Id
                        : c.MediaType == "movie" && localMediaMap[c.Id].MediaType == MediaType.Movie
                            ? localMediaMap[c.Id].Id
                            : null)
                    : null,
                TmdbId = c.Id,
                Title = c.Title ?? c.Name ?? "",
                PosterPath = c.PosterPath,
                Character = c.Character,
                MediaType = c.MediaType ?? "",
                ReleaseDate = c.ReleaseDate ?? c.FirstAirDate,
                VoteAverage = c.VoteAverage,
            })
            .ToList() ?? [];

        return Ok(new PersonCreditsDto
        {
            TmdbPersonId = tmdbPersonId,
            Name = personDetails?.Name ?? "",
            ProfilePath = personDetails?.ProfilePath,
            Credits = credits,
        });
    }
}
