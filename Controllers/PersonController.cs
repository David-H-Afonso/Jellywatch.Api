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

        // Build series query — optionally scoped to the profile's library
        var seriesQuery = _context.Series
            .Where(s => s.MediaItem.TmdbId.HasValue && tmdbIds.Contains(s.MediaItem.TmdbId.Value));

        if (profileId.HasValue)
        {
            seriesQuery = seriesQuery.Where(s =>
                s.MediaItem.WatchStates.Any(ws => ws.ProfileId == profileId.Value) ||
                s.Seasons.Any(sea => sea.Episodes.Any(ep =>
                    ep.WatchStates.Any(ws => ws.ProfileId == profileId.Value))));
        }

        // Map TMDB id → (series.Id for navigation, MediaItem.Id for poster)
        var localSeriesMap = await seriesQuery
            .Select(s => new { TmdbId = s.MediaItem.TmdbId!.Value, SeriesId = s.Id, AssetId = s.MediaItemId })
            .ToDictionaryAsync(s => s.TmdbId, s => (s.SeriesId, s.AssetId));

        // Build movie query — optionally scoped to the profile's library
        var movieQuery = _context.Movies
            .Where(m => m.MediaItem.TmdbId.HasValue && tmdbIds.Contains(m.MediaItem.TmdbId.Value));

        if (profileId.HasValue)
        {
            movieQuery = movieQuery.Where(m =>
                m.WatchStates.Any(ws => ws.ProfileId == profileId.Value));
        }

        // Map TMDB id → (movie.Id for navigation, MediaItem.Id for poster)
        var localMovieMap = await movieQuery
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
