using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure.ExternalServices;
using Jellywatch.Api.Infrastructure.Persistence;

namespace Jellywatch.Api.Controllers;

[Route("api/media")]
public class MediaAvailabilityController : BaseApiController
{
    private readonly JellywatchDbContext _context;
    private readonly RadarrApiClient _radarr;
    private readonly SonarrApiClient _sonarr;

    public MediaAvailabilityController(
        JellywatchDbContext context,
        RadarrApiClient radarr,
        SonarrApiClient sonarr)
    {
        _context = context;
        _radarr = radarr;
        _sonarr = sonarr;
    }

    /// <summary>
    /// Check if a media item is available in Sonarr/Radarr.
    /// </summary>
    [HttpGet("{mediaItemId:int}/availability")]
    public async Task<ActionResult<MediaAvailabilityDto?>> GetAvailability(int mediaItemId)
    {
        var mediaItem = await _context.MediaItems
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == mediaItemId);

        if (mediaItem is null)
            return NotFound(new { message = "Media item not found" });

        if (mediaItem.MediaType == MediaType.Movie)
        {
            if (!_radarr.IsConfigured || !mediaItem.TmdbId.HasValue)
                return Ok(new { configured = _radarr.IsConfigured, availability = (MediaAvailabilityDto?)null });

            var status = await _radarr.GetMovieStatusAsync(mediaItem.TmdbId.Value);
            if (status is null)
                return Ok(new { configured = true, availability = (MediaAvailabilityDto?)null });

            return Ok(new
            {
                configured = true,
                availability = new MediaAvailabilityDto
                {
                    Source = "radarr",
                    IsAvailable = status.HasFile,
                    IsMonitored = status.IsMonitored,
                    Status = status.Status,
                    SizeMb = status.SizeOnDisk,
                }
            });
        }

        if (mediaItem.MediaType == MediaType.Series)
        {
            if (!_sonarr.IsConfigured || !mediaItem.TvdbId.HasValue)
                return Ok(new { configured = _sonarr.IsConfigured, availability = (MediaAvailabilityDto?)null });

            var status = await _sonarr.GetSeriesStatusAsync(mediaItem.TvdbId.Value);
            if (status is null)
                return Ok(new { configured = true, availability = (MediaAvailabilityDto?)null });

            return Ok(new
            {
                configured = true,
                availability = new MediaAvailabilityDto
                {
                    Source = "sonarr",
                    IsAvailable = status.HasFile,
                    IsMonitored = status.IsMonitored,
                    Status = status.Status,
                    SizeMb = status.SizeOnDisk,
                    EpisodeFileCount = status.EpisodeFileCount,
                    TotalEpisodeCount = status.TotalEpisodeCount,
                }
            });
        }

        return Ok(new { configured = false, availability = (MediaAvailabilityDto?)null });
    }

    /// <summary>
    /// Returns whether Sonarr and Radarr are configured.
    /// </summary>
    [HttpGet("availability/status")]
    public ActionResult GetArrStatus()
    {
        return Ok(new
        {
            sonarrConfigured = _sonarr.IsConfigured,
            radarrConfigured = _radarr.IsConfigured,
        });
    }
}
