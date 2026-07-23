using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Jellywatch.Api.Application;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Common;
using Jellywatch.Api.Configuration;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure.Persistence;

namespace Jellywatch.Api.Controllers;

[ApiController]
[Route("api/integrations/household/v1")]
public sealed class HouseholdIntegrationController : ControllerBase
{
    private const int MaxActivityItems = 50;
    private const int MaxUpcomingDays = 90;
    private readonly JellywatchDbContext _context;
    private readonly IHouseholdIntegrationAccess _access;
    private readonly IHouseholdOAuthService _oauthService;
    private readonly IStatsService _statsService;
    private readonly IWatchStateService _watchStateService;
    private readonly IMediaQueryService _mediaQueryService;

    public HouseholdIntegrationController(
        JellywatchDbContext context,
        IHouseholdIntegrationAccess access,
        IHouseholdOAuthService oauthService,
        IStatsService statsService,
        IWatchStateService watchStateService,
        IMediaQueryService mediaQueryService)
    {
        _context = context;
        _access = access;
        _oauthService = oauthService;
        _statsService = statsService;
        _watchStateService = watchStateService;
        _mediaQueryService = mediaQueryService;
    }

    [Authorize]
    [EnableRateLimiting("household-authorize")]
    [HttpPost("authorize")]
    public async Task<IActionResult> Authorize([FromBody] HouseholdAuthorizeRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        var userId = HttpContext.GetUserId();
        if (!userId.HasValue) return Unauthorized();
        return ToOAuthResult(await _oauthService.AuthorizeAsync(userId.Value, request));
    }

    [AllowAnonymous]
    [EnableRateLimiting("household-token")]
    [HttpPost("token")]
    public async Task<IActionResult> Token([FromBody] HouseholdTokenRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        return ToOAuthResult(await _oauthService.ExchangeAsync(request));
    }

    [AllowAnonymous]
    [EnableRateLimiting("household-token")]
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke([FromBody] HouseholdRevokeRequest request)
    {
        Response.Headers.CacheControl = "no-store";
        await _oauthService.RevokeAsync(request.Token);
        return NoContent();
    }

    [AllowAnonymous]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        Response.Headers.CacheControl = "no-store";
        return ToOAuthResult(await _oauthService.GetMeAsync(HttpContext));
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<HouseholdDashboardDto>> GetDashboard(
        [FromQuery] int activityLimit = 20,
        [FromQuery] int upcomingDays = 30)
    {
        Response.Headers.CacheControl = "no-store";
        var access = await _access.AuthorizeAsync(
            HttpContext,
            HouseholdScopes.ProfileRead,
            HouseholdScopes.ActivityRead,
            HouseholdScopes.UpcomingRead);
        if (!access.IsAllowed) return StatusCode(access.StatusCode);

        activityLimit = Math.Clamp(activityLimit, 1, MaxActivityItems);
        upcomingDays = Math.Clamp(upcomingDays, 1, MaxUpcomingDays);
        var profileId = access.ProfileId!.Value;

        var profile = await _context.Profiles
            .AsNoTracking()
            .FirstAsync(p => p.Id == profileId);

        var summary = await BuildSummaryAsync(profileId, access.AccountId!, profile.DisplayName);
        var events = await _context.WatchEvents
            .AsNoTracking()
            .Where(e => e.ProfileId == profileId)
            .Include(e => e.MediaItem)
            .Include(e => e.Episode)
                .ThenInclude(episode => episode!.Season)
            .OrderByDescending(e => e.Timestamp)
            .Take(activityLimit)
            .ToListAsync();
        var mediaItemIds = events.Select(e => e.MediaItemId).Distinct().ToList();
        var watchStates = await _context.ProfileWatchStates
            .AsNoTracking()
            .Where(state => state.ProfileId == profileId && mediaItemIds.Contains(state.MediaItemId))
            .ToListAsync();
        var tmdbRatings = await _context.ExternalRatings
            .AsNoTracking()
            .Where(rating => mediaItemIds.Contains(rating.MediaItemId) && rating.Provider == ExternalProvider.Tmdb)
            .ToDictionaryAsync(rating => rating.MediaItemId, rating => rating.Score);
        var activity = events.Select(e =>
        {
            decimal? userRating;
            if (e.EpisodeId.HasValue)
            {
                userRating = watchStates
                    .Where(state => state.EpisodeId == e.EpisodeId)
                    .OrderByDescending(state => state.LastUpdated)
                    .Select(state => state.UserRating)
                    .FirstOrDefault();
                userRating ??= watchStates
                    .Where(state => state.SeasonId == e.Episode!.SeasonId)
                    .OrderByDescending(state => state.LastUpdated)
                    .Select(state => state.UserRating)
                    .FirstOrDefault();
                userRating ??= watchStates
                    .Where(state => state.MediaItemId == e.MediaItemId
                        && state.EpisodeId == null
                        && state.SeasonId == null
                        && state.MovieId == null)
                    .OrderByDescending(state => state.LastUpdated)
                    .Select(state => state.UserRating)
                    .FirstOrDefault();
            }
            else
            {
                userRating = watchStates
                    .Where(state => state.MediaItemId == e.MediaItemId
                        && state.EpisodeId == null
                        && state.SeasonId == null)
                    .OrderByDescending(state => state.LastUpdated)
                    .Select(state => state.UserRating)
                    .FirstOrDefault();
            }

            tmdbRatings.TryGetValue(e.MediaItemId, out var tmdbScore);
            return new HouseholdActivityItemDto
            {
                EventId = e.Id,
                MediaItemId = e.MediaItemId,
                Title = e.MediaItem.Title,
                MediaType = e.MediaItem.MediaType == MediaType.Movie ? "movie" : "series",
                EpisodeId = e.EpisodeId,
                EpisodeName = e.Episode?.Name,
                SeasonNumber = e.Episode?.Season.SeasonNumber,
                EpisodeNumber = e.Episode?.EpisodeNumber,
                EventType = e.EventType.ToString(),
                Timestamp = e.Timestamp,
                PosterUrl = $"/api/asset/{e.MediaItemId}/poster",
                UserRating = userRating,
                TmdbRating = e.Episode?.TmdbRating ?? ParseRating(tmdbScore),
            };
        }).ToList();

        var upcomingResult = await _statsService.GetUpcomingAsync(profileId, upcomingDays, access.UserId);
        var upcoming = upcomingResult.Success && upcomingResult.Data is not null
            ? upcomingResult.Data.Select(item => new HouseholdUpcomingItemDto
            {
                MediaItemId = item.MediaItemId,
                SeriesId = item.SeriesId,
                SeriesTitle = item.SeriesTitle,
                SeasonNumber = item.SeasonNumber,
                EpisodeNumber = item.EpisodeNumber,
                EpisodeName = item.EpisodeName,
                AirDate = item.AirDate,
                AirTime = item.AirTime,
                AirTimeUtc = item.AirTimeUtc,
                BatchCount = item.BatchCount,
                PosterUrl = $"/api/asset/{item.MediaItemId}/poster",
            }).ToList()
            : new List<HouseholdUpcomingItemDto>();

        return Ok(new HouseholdDashboardDto
        {
            Profile = summary,
            Activity = activity,
            Upcoming = upcoming,
        });
    }

    [HttpPatch("state")]
    public async Task<IActionResult> WriteState([FromBody] HouseholdStateWriteDto dto)
    {
        Response.Headers.CacheControl = "no-store";
        var access = await _access.AuthorizeAsync(HttpContext, HouseholdScopes.StateWrite);
        if (!access.IsAllowed) return StatusCode(access.StatusCode);
        if (!Enum.IsDefined(dto.State)) return BadRequest(new { message = "Unsupported state" });

        var result = dto.TargetType.ToLowerInvariant() switch
        {
            "movie" => await _watchStateService.UpdateMovieStateAsync(access.ProfileId!.Value, dto.TargetId, ToStateDto(dto)),
            "episode" => await _watchStateService.UpdateEpisodeStateAsync(access.ProfileId!.Value, dto.TargetId, ToStateDto(dto)),
            "season" => await _watchStateService.UpdateSeasonStateAsync(access.ProfileId!.Value, dto.TargetId, ToStateDto(dto)),
            "series" => await _watchStateService.UpdateSeriesStateAsync(access.ProfileId!.Value, dto.TargetId, ToStateDto(dto)),
            _ => (false, string.Empty, "Unsupported state target"),
        };

        return result.Item1 ? Ok(new { state = dto.State.ToString() }) : NotFound(new { message = result.Item3 });
    }

    [HttpPatch("rating")]
    public async Task<IActionResult> WriteRating([FromBody] HouseholdRatingWriteDto dto)
    {
        Response.Headers.CacheControl = "no-store";
        var access = await _access.AuthorizeAsync(HttpContext, HouseholdScopes.RatingWrite);
        if (!access.IsAllowed) return StatusCode(access.StatusCode);

        var profileId = access.ProfileId!.Value;
        var targetType = dto.TargetType.ToLowerInvariant();
        var result = targetType switch
        {
            "movie" => await _mediaQueryService.RateMovieAsync(dto.TargetId, profileId, new UserRatingDto { Rating = dto.Rating }),
            "series" => await _mediaQueryService.RateSeriesAsync(dto.TargetId, profileId, new UserRatingDto { Rating = dto.Rating }),
            "episode" when dto.SeriesId.HasValue => await _mediaQueryService.RateEpisodeAsync(dto.SeriesId.Value, dto.TargetId, profileId, new UserRatingDto { Rating = dto.Rating }),
            "season" when dto.SeriesId.HasValue => await _mediaQueryService.RateSeasonAsync(dto.SeriesId.Value, dto.TargetId, profileId, new UserRatingDto { Rating = dto.Rating }),
            _ => ServiceResult<object>.Fail("SeriesId is required for episode and season ratings.", 400),
        };

        return result.Success ? Ok(result.Data) : StatusCode(result.StatusCode ?? 400, new { message = result.Error });
    }

    private async Task<HouseholdProfileSummaryDto> BuildSummaryAsync(int profileId, string accountId, string displayName)
    {
        var totalMoviesSeen = await _context.ProfileWatchStates
            .Where(s => s.ProfileId == profileId && s.MovieId.HasValue && s.State == WatchState.Seen)
            .Select(s => s.MovieId!.Value)
            .Distinct()
            .CountAsync();
        var totalEpisodesSeen = await _context.ProfileWatchStates
            .Where(s => s.ProfileId == profileId && s.EpisodeId.HasValue && s.State == WatchState.Seen)
            .Select(s => s.EpisodeId!.Value)
            .Distinct()
            .CountAsync();
        var seriesWatching = await _context.Series
            .Where(s => s.Seasons.SelectMany(season => season.Episodes)
                .Any(ep => ep.WatchStates.Any(ws => ws.ProfileId == profileId && ws.State == WatchState.Seen))
                && s.Seasons.SelectMany(season => season.Episodes)
                    .Any(ep => !ep.WatchStates.Any(ws => ws.ProfileId == profileId
                        && (ws.State == WatchState.Seen || ws.State == WatchState.WontWatch))))
            .CountAsync();
        var completedSeries = await _context.Series
            .Where(s => s.Seasons.Any(season => season.Episodes.Any())
                && s.Seasons.SelectMany(season => season.Episodes).All(ep => ep.WatchStates.Any(ws =>
                    ws.ProfileId == profileId && (ws.State == WatchState.Seen || ws.State == WatchState.WontWatch)))
                && s.Seasons.SelectMany(season => season.Episodes).Any(ep => ep.WatchStates.Any(ws =>
                    ws.ProfileId == profileId && ws.State == WatchState.Seen)))
            .CountAsync();

        return new HouseholdProfileSummaryDto
        {
            Id = accountId,
            DisplayName = displayName,
            TotalSeriesWatching = seriesWatching,
            TotalSeriesCompleted = completedSeries,
            TotalMoviesSeen = totalMoviesSeen,
            TotalEpisodesSeen = totalEpisodesSeen,
        };
    }

    private static WatchStateUpdateDto ToStateDto(HouseholdStateWriteDto dto) => new()
    {
        State = dto.State,
        Timestamp = dto.Timestamp,
    };

    private static double? ParseRating(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var rating)
            ? rating
            : null;

    private ObjectResult ToOAuthResult<T>(HouseholdOperationResult<T> result)
    {
        if (result.Success) return StatusCode(result.StatusCode, result.Data);
        return StatusCode(result.StatusCode, new
        {
            error = result.Error,
            errorDescription = result.ErrorDescription,
        });
    }
}
