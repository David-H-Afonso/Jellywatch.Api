using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Helpers;
using Jellywatch.Api.Infrastructure;

namespace Jellywatch.Api.Controllers;

[Route("api/media/series")]
public class SeriesController : BaseApiController
{
    private readonly JellywatchDbContext _context;

    public SeriesController(JellywatchDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<SeriesListDto>>> GetSeries([FromQuery] MediaQueryParameters query)
    {
        var profileId = query.ProfileId;

        var baseQuery = _context.Series
            .Include(s => s.MediaItem)
            .AsQueryable();

        // Only show series that this profile has synced or manually added
        if (profileId.HasValue)
        {
            baseQuery = baseQuery.Where(s =>
                s.Seasons.Any(sea => sea.Episodes.Any(ep =>
                    ep.WatchStates.Any(ws => ws.ProfileId == profileId.Value))) ||
                s.MediaItem.WatchStates.Any(ws => ws.ProfileId == profileId.Value));

            // Exclude series blocked by this profile
            baseQuery = baseQuery.Where(s =>
                !_context.ProfileMediaBlocks.Any(b => b.ProfileId == profileId.Value && b.MediaItemId == s.MediaItemId));
        }

        // Search filter
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.ToLower();
            baseQuery = baseQuery.Where(s =>
                s.MediaItem.Title.ToLower().Contains(search) ||
                (s.MediaItem.OriginalTitle != null && s.MediaItem.OriginalTitle.ToLower().Contains(search)));
        }

        var projected = baseQuery.Select(s => new SeriesListDto
        {
            Id = s.Id,
            MediaItemId = s.MediaItemId,
            Title = s.MediaItem.Title,
            PosterPath = s.MediaItem.PosterPath,
            Status = s.MediaItem.Status,
            TotalSeasons = s.TotalSeasons,
            TotalEpisodes = s.TotalEpisodes,
            ReleaseDate = s.MediaItem.ReleaseDate,
            EpisodesSeen = profileId.HasValue
                ? s.Seasons.SelectMany(sea => sea.Episodes)
                    .Count(ep => ep.WatchStates.Any(ws => ws.ProfileId == profileId.Value && ws.State == WatchState.Seen))
                : 0,
            AggregateState = profileId.HasValue
                ? (s.Seasons.SelectMany(sea => sea.Episodes).All(ep =>
                    ep.WatchStates.Any(ws => ws.ProfileId == profileId.Value
                        && (ws.State == WatchState.Seen || ws.State == WatchState.WontWatch)))
                    && s.Seasons.SelectMany(sea => sea.Episodes).Any()
                    && s.Seasons.SelectMany(sea => sea.Episodes).Any(ep =>
                        ep.WatchStates.Any(ws => ws.ProfileId == profileId.Value && ws.State == WatchState.Seen))
                    ? WatchState.Seen
                    : s.Seasons.SelectMany(sea => sea.Episodes).Any(ep =>
                        ep.WatchStates.Any(ws => ws.ProfileId == profileId.Value
                            && ws.State != WatchState.Unseen && ws.State != WatchState.WontWatch))
                        ? WatchState.InProgress
                        : WatchState.Unseen)
                : WatchState.Unseen,
            UserRating = profileId.HasValue
                ? s.MediaItem.WatchStates.Where(ws => ws.ProfileId == profileId.Value && ws.EpisodeId == null && ws.MovieId == null)
                    .Select(ws => ws.UserRating).FirstOrDefault()
                : null
        });

        // State filter
        if (!string.IsNullOrWhiteSpace(query.State) && Enum.TryParse<WatchState>(query.State, true, out var stateFilter))
        {
            projected = projected.Where(s => s.AggregateState == stateFilter);
        }

        // Sorting
        projected = query.SortBy?.ToLower() switch
        {
            "title" => query.SortDescending ? projected.OrderByDescending(s => s.Title) : projected.OrderBy(s => s.Title),
            "release" => query.SortDescending ? projected.OrderByDescending(s => s.ReleaseDate) : projected.OrderBy(s => s.ReleaseDate),
            "grade" => query.SortDescending ? projected.OrderByDescending(s => s.UserRating) : projected.OrderBy(s => s.UserRating),
            _ => projected.OrderBy(s => s.Title)
        };

        var totalCount = await projected.CountAsync();
        var data = await projected
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync();

        return Ok(new PagedResult<SeriesListDto>
        {
            Data = data,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SeriesDetailDto>> GetSeriesDetail(int id, [FromQuery] int? profileId)
    {
        var series = await _context.Series
            .Include(s => s.MediaItem)
                .ThenInclude(m => m.ExternalRatings)
            .Include(s => s.MediaItem)
                .ThenInclude(m => m.Translations)
            .Include(s => s.Seasons.OrderBy(sea => sea.SeasonNumber))
                .ThenInclude(sea => sea.Episodes.OrderBy(e => e.EpisodeNumber))
            .FirstOrDefaultAsync(s => s.Id == id);

        if (series is null)
            return NotFound(new { message = "Series not found" });

        // Load watch states for profile if specified
        List<ProfileWatchState> watchStates = new();
        List<ProfileWatchState> seasonRatings = new();
        decimal? userRating = null;
        Dictionary<int, DateTime?> episodeWatchedAt = new();
        if (profileId.HasValue)
        {
            var episodeIds = series.Seasons.SelectMany(s => s.Episodes).Select(e => e.Id).ToList();
            watchStates = await _context.ProfileWatchStates
                .Where(ws => ws.ProfileId == profileId.Value && ws.EpisodeId.HasValue && episodeIds.Contains(ws.EpisodeId.Value))
                .ToListAsync();

            var watchedAtEntries = await _context.WatchEvents
                .Where(e => e.ProfileId == profileId.Value
                    && e.EpisodeId.HasValue
                    && episodeIds.Contains(e.EpisodeId!.Value)
                    && e.EventType == WatchEventType.Finished)
                .GroupBy(e => e.EpisodeId!.Value)
                .Select(g => new { EpisodeId = g.Key, WatchedAt = g.Max(e => e.Timestamp) })
                .ToListAsync();
            episodeWatchedAt = watchedAtEntries.ToDictionary(e => e.EpisodeId, e => (DateTime?)e.WatchedAt);

            var seasonIds = series.Seasons.Select(s => s.Id).ToList();
            seasonRatings = await _context.ProfileWatchStates
                .Where(ws => ws.ProfileId == profileId.Value && ws.SeasonId.HasValue && seasonIds.Contains(ws.SeasonId.Value))
                .ToListAsync();

            var seriesWs = await _context.ProfileWatchStates
                .FirstOrDefaultAsync(ws => ws.ProfileId == profileId.Value
                    && ws.MediaItemId == series.MediaItemId
                    && ws.EpisodeId == null && ws.SeasonId == null && ws.MovieId == null);
            userRating = seriesWs?.UserRating;
        }

        var dto = new SeriesDetailDto
        {
            Id = series.Id,
            MediaItemId = series.MediaItemId,
            Title = series.MediaItem.Title,
            OriginalTitle = series.MediaItem.OriginalTitle,
            Overview = series.MediaItem.Overview,
            PosterPath = series.MediaItem.PosterPath,
            BackdropPath = series.MediaItem.BackdropPath,
            ReleaseDate = series.MediaItem.ReleaseDate,
            Status = series.MediaItem.Status,
            OriginalLanguage = series.MediaItem.OriginalLanguage,
            Network = series.Network,
            TotalSeasons = series.TotalSeasons,
            TotalEpisodes = series.TotalEpisodes,
            UserRating = userRating,
            IsBlocked = profileId.HasValue && await _context.ProfileMediaBlocks
                .AnyAsync(b => b.ProfileId == profileId.Value && b.MediaItemId == series.MediaItemId),
            Ratings = series.MediaItem.ExternalRatings.Select(r => new ExternalRatingDto
            {
                Provider = r.Provider,
                Score = r.Score,
                VoteCount = r.VoteCount
            }).ToList(),
            Seasons = series.Seasons.Select(sea => new SeasonDto
            {
                Id = sea.Id,
                SeasonNumber = sea.SeasonNumber,
                Name = sea.Name,
                Overview = sea.Overview,
                PosterPath = sea.PosterPath,
                PosterUrl = sea.PosterPath != null ? $"https://image.tmdb.org/t/p/w342{sea.PosterPath}" : null,
                EpisodeCount = sea.EpisodeCount,
                AirDate = sea.AirDate,
                TmdbRating = sea.TmdbRating,
                EpisodesSeen = watchStates.Count(ws => ws.EpisodeId.HasValue
                    && sea.Episodes.Any(e => e.Id == ws.EpisodeId.Value)
                    && ws.State == WatchState.Seen),
                UserRating = seasonRatings.FirstOrDefault(sr => sr.SeasonId == sea.Id)?.UserRating,
                Episodes = sea.Episodes.Select(ep =>
                {
                    var ws = watchStates.FirstOrDefault(w => w.EpisodeId == ep.Id);
                    return new EpisodeDto
                    {
                        Id = ep.Id,
                        EpisodeNumber = ep.EpisodeNumber,
                        Name = ep.Name,
                        Overview = ep.Overview,
                        StillPath = ep.StillPath,
                        StillUrl = ep.StillPath != null ? $"https://image.tmdb.org/t/p/w300{ep.StillPath}" : null,
                        AirDate = ep.AirDate,
                        Runtime = ep.Runtime,
                        TmdbRating = ep.TmdbRating,
                        State = ws?.State ?? WatchState.Unseen,
                        IsManualOverride = ws?.IsManualOverride ?? false,
                        UserRating = ws?.UserRating,
                        WatchedAt = episodeWatchedAt.GetValueOrDefault(ep.Id)
                    };
                }).ToList()
            }).ToList(),
            SpanishTranslation = series.MediaItem.Translations
                .Where(t => t.Language.StartsWith("es"))
                .Select(t => new TranslationDto
                {
                    Language = t.Language,
                    Title = t.Title,
                    Overview = t.Overview
                }).FirstOrDefault()
        };

        return Ok(dto);
    }

    [HttpGet("{id:int}/seasons")]
    public async Task<ActionResult<List<SeasonDto>>> GetSeasons(int id, [FromQuery] int? profileId)
    {
        var seasons = await _context.Seasons
            .Where(s => s.SeriesId == id)
            .OrderBy(s => s.SeasonNumber)
            .Include(s => s.Episodes.OrderBy(e => e.EpisodeNumber))
            .ToListAsync();

        if (seasons.Count == 0)
            return NotFound(new { message = "Series not found or has no seasons" });

        List<ProfileWatchState> watchStates = new();
        if (profileId.HasValue)
        {
            var episodeIds = seasons.SelectMany(s => s.Episodes).Select(e => e.Id).ToList();
            watchStates = await _context.ProfileWatchStates
                .Where(ws => ws.ProfileId == profileId.Value && ws.EpisodeId.HasValue && episodeIds.Contains(ws.EpisodeId.Value))
                .ToListAsync();
        }

        var dtos = seasons.Select(sea => new SeasonDto
        {
            Id = sea.Id,
            SeasonNumber = sea.SeasonNumber,
            Name = sea.Name,
            Overview = sea.Overview,
            PosterPath = sea.PosterPath,
            PosterUrl = sea.PosterPath != null ? $"https://image.tmdb.org/t/p/w342{sea.PosterPath}" : null,
            EpisodeCount = sea.EpisodeCount,
            AirDate = sea.AirDate,
            TmdbRating = sea.TmdbRating,
            EpisodesSeen = watchStates.Count(ws => ws.EpisodeId.HasValue
                && sea.Episodes.Any(e => e.Id == ws.EpisodeId.Value)
                && ws.State == WatchState.Seen),
            Episodes = sea.Episodes.Select(ep =>
            {
                var ws = watchStates.FirstOrDefault(w => w.EpisodeId == ep.Id);
                return new EpisodeDto
                {
                    Id = ep.Id,
                    EpisodeNumber = ep.EpisodeNumber,
                    Name = ep.Name,
                    Overview = ep.Overview,
                    StillPath = ep.StillPath,
                    StillUrl = ep.StillPath != null ? $"https://image.tmdb.org/t/p/w300{ep.StillPath}" : null,
                    AirDate = ep.AirDate,
                    Runtime = ep.Runtime,
                    TmdbRating = ep.TmdbRating,
                    State = ws?.State ?? WatchState.Unseen,
                    IsManualOverride = ws?.IsManualOverride ?? false
                };
            }).ToList()
        }).ToList();

        return Ok(dtos);
    }

    [HttpGet("seasons/{seasonId:int}/episodes")]
    public async Task<ActionResult<List<EpisodeDto>>> GetEpisodes(int seasonId, [FromQuery] int? profileId)
    {
        var episodes = await _context.Episodes
            .Where(e => e.SeasonId == seasonId)
            .OrderBy(e => e.EpisodeNumber)
            .ToListAsync();

        if (episodes.Count == 0)
            return NotFound(new { message = "Season not found or has no episodes" });

        List<ProfileWatchState> watchStates = new();
        if (profileId.HasValue)
        {
            var episodeIds = episodes.Select(e => e.Id).ToList();
            watchStates = await _context.ProfileWatchStates
                .Where(ws => ws.ProfileId == profileId.Value && ws.EpisodeId.HasValue && episodeIds.Contains(ws.EpisodeId.Value))
                .ToListAsync();
        }

        var dtos = episodes.Select(ep =>
        {
            var ws = watchStates.FirstOrDefault(w => w.EpisodeId == ep.Id);
            return new EpisodeDto
            {
                Id = ep.Id,
                EpisodeNumber = ep.EpisodeNumber,
                Name = ep.Name,
                Overview = ep.Overview,
                StillPath = ep.StillPath,
                StillUrl = ep.StillPath != null ? $"https://image.tmdb.org/t/p/w300{ep.StillPath}" : null,
                AirDate = ep.AirDate,
                Runtime = ep.Runtime,
                TmdbRating = ep.TmdbRating,
                State = ws?.State ?? WatchState.Unseen,
                IsManualOverride = ws?.IsManualOverride ?? false
            };
        }).ToList();

        return Ok(dtos);
    }

    [HttpPatch("{id:int}/rating")]
    public async Task<IActionResult> RateSeries(int id, [FromQuery] int profileId, [FromBody] UserRatingDto dto)
    {
        var series = await _context.Series.FirstOrDefaultAsync(s => s.Id == id);
        if (series is null) return NotFound(new { message = "Series not found" });

        var ws = await _context.ProfileWatchStates
            .FirstOrDefaultAsync(w => w.ProfileId == profileId
                && w.MediaItemId == series.MediaItemId
                && w.EpisodeId == null && w.SeasonId == null && w.MovieId == null);

        if (ws is null)
        {
            ws = new ProfileWatchState
            {
                ProfileId = profileId,
                MediaItemId = series.MediaItemId,
                State = WatchState.Unseen,
                UserRating = dto.Rating,
                LastUpdated = DateTime.UtcNow
            };
            _context.ProfileWatchStates.Add(ws);
        }
        else
        {
            ws.UserRating = dto.Rating;
        }

        await _context.SaveChangesAsync();
        return Ok(new { userRating = dto.Rating });
    }

    [HttpPatch("{id:int}/episodes/{episodeId:int}/rating")]
    public async Task<IActionResult> RateEpisode(int id, int episodeId, [FromQuery] int profileId, [FromBody] UserRatingDto dto)
    {
        var episode = await _context.Episodes
            .Include(e => e.Season)
            .FirstOrDefaultAsync(e => e.Id == episodeId && e.Season.SeriesId == id);

        if (episode is null) return NotFound(new { message = "Episode not found" });

        var series = await _context.Series.FirstOrDefaultAsync(s => s.Id == id);
        if (series is null) return NotFound(new { message = "Series not found" });

        var ws = await _context.ProfileWatchStates
            .FirstOrDefaultAsync(w => w.ProfileId == profileId && w.EpisodeId == episodeId);

        if (ws is null)
        {
            ws = new ProfileWatchState
            {
                ProfileId = profileId,
                MediaItemId = series.MediaItemId,
                EpisodeId = episodeId,
                State = WatchState.Unseen,
                UserRating = dto.Rating,
                LastUpdated = DateTime.UtcNow
            };
            _context.ProfileWatchStates.Add(ws);
        }
        else
        {
            ws.UserRating = dto.Rating;
        }

        await _context.SaveChangesAsync();
        return Ok(new { userRating = dto.Rating });
    }

    [HttpPatch("{id:int}/seasons/{seasonId:int}/rating")]
    public async Task<IActionResult> RateSeason(int id, int seasonId, [FromQuery] int profileId, [FromBody] UserRatingDto dto)
    {
        var season = await _context.Seasons
            .FirstOrDefaultAsync(s => s.Id == seasonId && s.SeriesId == id);

        if (season is null) return NotFound(new { message = "Season not found" });

        var series = await _context.Series.FirstOrDefaultAsync(s => s.Id == id);
        if (series is null) return NotFound(new { message = "Series not found" });

        var ws = await _context.ProfileWatchStates
            .FirstOrDefaultAsync(w => w.ProfileId == profileId
                && w.MediaItemId == series.MediaItemId
                && w.SeasonId == seasonId);

        if (ws is null)
        {
            ws = new ProfileWatchState
            {
                ProfileId = profileId,
                MediaItemId = series.MediaItemId,
                SeasonId = seasonId,
                State = WatchState.Unseen,
                UserRating = dto.Rating,
                LastUpdated = DateTime.UtcNow
            };
            _context.ProfileWatchStates.Add(ws);
        }
        else
        {
            ws.UserRating = dto.Rating;
        }

        await _context.SaveChangesAsync();
        return Ok(new { userRating = dto.Rating });
    }
}
