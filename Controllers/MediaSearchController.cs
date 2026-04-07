using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure;
using Jellywatch.Api.Services.Metadata;

namespace Jellywatch.Api.Controllers;

[Route("api/media/search")]
public class MediaSearchController : BaseApiController
{
    private readonly JellywatchDbContext _context;
    private readonly ITmdbApiClient _tmdbClient;
    private readonly IMetadataResolutionService _metadata;

    public MediaSearchController(
        JellywatchDbContext context,
        ITmdbApiClient tmdbClient,
        IMetadataResolutionService metadata)
    {
        _context = context;
        _tmdbClient = tmdbClient;
        _metadata = metadata;
    }

    [HttpGet("tmdb")]
    public async Task<ActionResult> SearchTmdb([FromQuery] string query, [FromQuery] string type = "series", [FromQuery] int? year = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(new { message = "Query is required" });

        if (!_tmdbClient.IsConfigured)
            return BadRequest(new { message = "TMDB is not configured" });

        if (type == "movie")
        {
            var results = await _tmdbClient.SearchMovieAsync(query, year);
            return Ok(results);
        }

        var tvResults = await _tmdbClient.SearchTvAsync(query, year);
        return Ok(tvResults);
    }

    [HttpPost("add")]
    public async Task<ActionResult> AddManually([FromBody] ManualAddDto dto)
    {
        if (dto.TmdbId <= 0)
            return BadRequest(new { message = "A valid TMDB ID is required" });

        if (dto.Type != "series" && dto.Type != "movie")
            return BadRequest(new { message = "Type must be 'series' or 'movie'" });

        if (dto.ProfileId <= 0)
            return BadRequest(new { message = "A valid ProfileId is required" });

        var profile = await _context.Profiles.FindAsync(dto.ProfileId);
        if (profile is null)
            return NotFound(new { message = "Profile not found" });

        if (dto.Type == "series")
        {
            return await AddSeriesManuallyAsync(dto.TmdbId, dto.ProfileId);
        }

        return await AddMovieManuallyAsync(dto.TmdbId, dto.ProfileId);
    }

    private async Task<ActionResult> AddSeriesManuallyAsync(int tmdbId, int profileId)
    {
        var syntheticId = $"manual_series_{tmdbId}";

        var mediaItem = await _metadata.ResolveSeriesAsync(syntheticId, "Manual", tmdbId: tmdbId);
        if (mediaItem is null)
            return NotFound(new { message = $"Could not find series with TMDB ID {tmdbId}" });

        var series = await _context.Series.FirstOrDefaultAsync(s => s.MediaItemId == mediaItem.Id);
        if (series != null)
            await _metadata.PopulateSeasonsAndEpisodesAsync(series.Id);

        await _metadata.RefreshTranslationsAsync(mediaItem.Id);
        await _metadata.RefreshImagesAsync(mediaItem.Id);

        // Create a ProfileWatchState entry so this series shows up for the profile
        var existingState = await _context.ProfileWatchStates
            .AnyAsync(ws => ws.ProfileId == profileId && ws.MediaItemId == mediaItem.Id && ws.EpisodeId == null && ws.MovieId == null);

        if (!existingState)
        {
            _context.ProfileWatchStates.Add(new ProfileWatchState
            {
                ProfileId = profileId,
                MediaItemId = mediaItem.Id,
                State = WatchState.Unseen,
            });
            await _context.SaveChangesAsync();
        }

        return Ok(new
        {
            message = "Series added successfully",
            mediaItemId = mediaItem.Id,
            seriesId = series?.Id,
            title = mediaItem.Title
        });
    }

    private async Task<ActionResult> AddMovieManuallyAsync(int tmdbId, int profileId)
    {
        var syntheticId = $"manual_movie_{tmdbId}";

        var mediaItem = await _metadata.ResolveMovieAsync(syntheticId, "Manual", tmdbId: tmdbId);
        if (mediaItem is null)
            return NotFound(new { message = $"Could not find movie with TMDB ID {tmdbId}" });

        await _metadata.RefreshTranslationsAsync(mediaItem.Id);
        await _metadata.RefreshImagesAsync(mediaItem.Id);

        var movie = await _context.Movies.FirstOrDefaultAsync(m => m.MediaItemId == mediaItem.Id);

        // Create a ProfileWatchState entry so this movie shows up for the profile
        var existingState = await _context.ProfileWatchStates
            .AnyAsync(ws => ws.ProfileId == profileId && ws.MovieId == movie!.Id);

        if (!existingState && movie != null)
        {
            _context.ProfileWatchStates.Add(new ProfileWatchState
            {
                ProfileId = profileId,
                MediaItemId = mediaItem.Id,
                MovieId = movie.Id,
                State = WatchState.Unseen,
            });
            await _context.SaveChangesAsync();
        }

        return Ok(new
        {
            message = "Movie added successfully",
            mediaItemId = mediaItem.Id,
            movieId = movie?.Id,
            title = mediaItem.Title
        });
    }
}

public class ManualAddDto
{
    public int TmdbId { get; set; }
    public string Type { get; set; } = "series";
    public int ProfileId { get; set; }
}
