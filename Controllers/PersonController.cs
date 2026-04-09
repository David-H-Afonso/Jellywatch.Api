using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
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
    public async Task<ActionResult<PersonCreditsDto>> GetPersonCredits(int tmdbPersonId, [FromQuery] int? profileId = null)
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

        // Get ALL series in DB matching these TMDB ids (regardless of profile)
        var localSeriesMap = await _context.Series
            .Where(s => s.MediaItem.TmdbId.HasValue && tmdbIds.Contains(s.MediaItem.TmdbId.Value))
            .Select(s => new { TmdbId = s.MediaItem.TmdbId!.Value, SeriesId = s.Id, AssetId = s.MediaItemId })
            .ToDictionaryAsync(s => s.TmdbId, s => (s.SeriesId, s.AssetId));

        // Which MediaItemIds does the current profile have?
        var profileMediaItemIds = new HashSet<int>();
        if (profileId.HasValue)
        {
            profileMediaItemIds = (await _context.ProfileWatchStates
                .Where(ws => ws.ProfileId == profileId.Value)
                .Select(ws => ws.MediaItemId)
                .Distinct()
                .ToListAsync()).ToHashSet();
        }

        // Get ALL movies in DB matching these TMDB ids (regardless of profile)
        var localMovieMap = await _context.Movies
            .Where(m => m.MediaItem.TmdbId.HasValue && tmdbIds.Contains(m.MediaItem.TmdbId.Value))
            .Select(m => new { TmdbId = m.MediaItem.TmdbId!.Value, MovieId = m.Id, AssetId = m.MediaItemId })
            .ToDictionaryAsync(m => m.TmdbId, m => (m.MovieId, m.AssetId));

        var credits = tmdbCredits.Cast?
            .OrderByDescending(c => c.VoteAverage ?? 0)
            .ThenByDescending(c => c.ReleaseDate ?? c.FirstAirDate ?? "")
            .Select(c => new PersonCreditItemDto
            {
                LocalMediaItemId = c.MediaType == "tv" && localSeriesMap.ContainsKey(c.Id)
                    ? localSeriesMap[c.Id].SeriesId
                    : c.MediaType == "movie" && localMovieMap.ContainsKey(c.Id)
                        ? localMovieMap[c.Id].MovieId
                        : null,
                LocalAssetId = c.MediaType == "tv" && localSeriesMap.ContainsKey(c.Id)
                    ? localSeriesMap[c.Id].AssetId
                    : c.MediaType == "movie" && localMovieMap.ContainsKey(c.Id)
                        ? localMovieMap[c.Id].AssetId
                        : null,
                IsInYourLibrary = (c.MediaType == "tv" && localSeriesMap.ContainsKey(c.Id) && profileMediaItemIds.Contains(localSeriesMap[c.Id].AssetId)) ||
                                  (c.MediaType == "movie" && localMovieMap.ContainsKey(c.Id) && profileMediaItemIds.Contains(localMovieMap[c.Id].AssetId)),
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
