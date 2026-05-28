using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Common;
using Jellywatch.Api.Infrastructure.Persistence;
using Jellywatch.Api.Infrastructure.ExternalServices;

namespace Jellywatch.Api.Application.Services;

public class MediaQueryService : IMediaQueryService
{
    private const decimal MinUserRating = 0m;
    private const decimal MaxUserRating = 10m;
    private readonly JellywatchDbContext _context;
    private readonly ITmdbApiClient _tmdbClient;

    public MediaQueryService(JellywatchDbContext context, ITmdbApiClient tmdbClient)
    {
        _context = context;
        _tmdbClient = tmdbClient;
    }

    // ── Series ──────────────────────────────────────────────────────────────

    public async Task<ServiceResult<PagedResult<SeriesListDto>>> GetSeriesAsync(MediaQueryParameters query)
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
                : null,
            TmdbRating = s.MediaItem.ExternalRatings
                .Where(er => er.Provider == ExternalProvider.Tmdb)
                .Select(er => er.Score != null ? (double?)Convert.ToDouble(er.Score) : null)
                .FirstOrDefault()
        });

        // State filter
        if (!string.IsNullOrWhiteSpace(query.State) && Enum.TryParse<WatchState>(query.State, true, out var stateFilter))
        {
            projected = projected.Where(s => s.AggregateState == stateFilter);
        }

        int totalCount;
        List<SeriesListDto> data;

        var sortKey = query.SortBy?.ToLower();
        if (sortKey == "top" || sortKey == "grade")
        {
            var allData = await projected.ToListAsync();
            totalCount = allData.Count;
            IEnumerable<SeriesListDto> sorted = sortKey switch
            {
                "top" => query.SortDescending
                    ? allData
                        .OrderBy(s => s.UserRating.HasValue ? 0 : s.TmdbRating.HasValue ? 1 : 2)
                        .ThenByDescending(s => s.UserRating ?? 0)
                        .ThenByDescending(s => s.TmdbRating ?? 0)
                        .ThenByDescending(s => s.ReleaseDate)
                    : allData
                        .OrderByDescending(s => s.UserRating.HasValue ? 0 : s.TmdbRating.HasValue ? 1 : 2)
                        .ThenBy(s => s.UserRating ?? 0)
                        .ThenBy(s => s.TmdbRating ?? 0)
                        .ThenBy(s => s.ReleaseDate),
                _ => query.SortDescending // grade
                    ? allData
                        .OrderBy(s => s.UserRating.HasValue ? 0 : 1)
                        .ThenByDescending(s => s.UserRating ?? 0)
                    : allData
                        .OrderByDescending(s => s.UserRating.HasValue ? 0 : 1)
                        .ThenBy(s => s.UserRating ?? 0),
            };
            data = sorted.Skip(query.Skip).Take(query.Take).ToList();
        }
        else
        {
            projected = query.SortBy?.ToLower() switch
            {
                "title" => query.SortDescending ? projected.OrderByDescending(s => s.Title) : projected.OrderBy(s => s.Title),
                "release" => query.SortDescending ? projected.OrderByDescending(s => s.ReleaseDate) : projected.OrderBy(s => s.ReleaseDate),
                _ => projected.OrderBy(s => s.Title)
            };

            totalCount = await projected.CountAsync();
            data = await projected
                .Skip(query.Skip)
                .Take(query.Take)
                .ToListAsync();
        }

        return ServiceResult<PagedResult<SeriesListDto>>.Ok(new PagedResult<SeriesListDto>
        {
            Data = data,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        });
    }

    public async Task<ServiceResult<SeriesDetailDto>> GetSeriesDetailAsync(int id, int? profileId, int? currentUserId)
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
            return ServiceResult<SeriesDetailDto>.Fail("Series not found", 404);

        // Load watch states for profile if specified
        List<ProfileWatchState> watchStates = new();
        List<ProfileWatchState> seasonRatings = new();
        decimal? userRating = null;
        bool includeInDashboard = false;
        bool excludeFromDashboard = false;
        bool isInDashboard = false;
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

            var dashboardStates = await _context.ProfileWatchStates
                .Where(ws => ws.ProfileId == profileId.Value && ws.MediaItemId == series.MediaItemId)
                .ToListAsync();
            var seriesWs = dashboardStates.FirstOrDefault(ws =>
                ws.EpisodeId == null && ws.SeasonId == null && ws.MovieId == null);
            userRating = seriesWs?.UserRating;
            includeInDashboard = seriesWs?.IncludeInDashboard == true;
            excludeFromDashboard = dashboardStates.Any(ws => ws.ExcludeFromDashboard);

            var hasDashboardState = dashboardStates.Any(ws =>
                ws.State == WatchState.InProgress || ws.State == WatchState.Seen || ws.IncludeInDashboard);
            var hasWatchEvents = await _context.WatchEvents
                .AnyAsync(e => e.ProfileId == profileId.Value
                    && e.MediaItemId == series.MediaItemId
                    && e.EpisodeId != null);
            var hasWatchlistSignal = currentUserId.HasValue
                && await IsSeriesIncludedByWatchlistAsync(currentUserId.Value, profileId.Value, series.MediaItemId);
            isInDashboard = !excludeFromDashboard && (hasDashboardState || hasWatchEvents || hasWatchlistSignal);
        }

        var dto = new SeriesDetailDto
        {
            Id = series.Id,
            MediaItemId = series.MediaItemId,
            TmdbId = series.MediaItem.TmdbId,
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
            Genres = series.MediaItem.Genres,
            UserRating = userRating,
            IsBlocked = profileId.HasValue && await _context.ProfileMediaBlocks
                .AnyAsync(b => b.ProfileId == profileId.Value && b.MediaItemId == series.MediaItemId),
            IsInLibrary = !profileId.HasValue || await _context.ProfileWatchStates
                .AnyAsync(ws => ws.ProfileId == profileId.Value && ws.MediaItemId == series.MediaItemId),
            IncludeInDashboard = includeInDashboard,
            ExcludeFromDashboard = excludeFromDashboard,
            IsInDashboard = isInDashboard,
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
                PosterUrl = BuildTmdbImageUrl(sea.PosterPath, "w342"),
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
                        StillUrl = BuildTmdbImageUrl(ep.StillPath, "w300"),
                        AirDate = ep.AirDate,
                        Runtime = ep.Runtime,
                        TmdbRating = ep.TmdbRating,
                        State = ws?.State ?? WatchState.Unseen,
                        IsManualOverride = ws?.IsManualOverride ?? false,
                        UserRating = ws?.UserRating,
                        WatchedAt = (ws?.State == WatchState.Seen || ws?.State == WatchState.WontWatch) ? episodeWatchedAt.GetValueOrDefault(ep.Id) : null
                    };
                }).ToList()
            }).ToList(),
            SpanishTranslation = series.MediaItem.Translations
                .Where(t => !string.IsNullOrWhiteSpace(t.Language)
                    && t.Language.StartsWith("es", StringComparison.OrdinalIgnoreCase))
                .Select(t => new TranslationDto
                {
                    Language = t.Language,
                    Title = t.Title,
                    Overview = t.Overview
                }).FirstOrDefault()
        };

        return ServiceResult<SeriesDetailDto>.Ok(dto);
    }

    public async Task<ServiceResult<List<SeasonDto>>> GetSeasonsAsync(int seriesId, int? profileId)
    {
        var seasons = await _context.Seasons
            .Where(s => s.SeriesId == seriesId)
            .OrderBy(s => s.SeasonNumber)
            .Include(s => s.Episodes.OrderBy(e => e.EpisodeNumber))
            .ToListAsync();

        if (seasons.Count == 0)
            return ServiceResult<List<SeasonDto>>.Fail("Series not found or has no seasons", 404);

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
            PosterUrl = BuildTmdbImageUrl(sea.PosterPath, "w342"),
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
                    StillUrl = BuildTmdbImageUrl(ep.StillPath, "w300"),
                    AirDate = ep.AirDate,
                    Runtime = ep.Runtime,
                    TmdbRating = ep.TmdbRating,
                    State = ws?.State ?? WatchState.Unseen,
                    IsManualOverride = ws?.IsManualOverride ?? false
                };
            }).ToList()
        }).ToList();

        return ServiceResult<List<SeasonDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<List<EpisodeDto>>> GetEpisodesAsync(int seasonId, int? profileId)
    {
        var episodes = await _context.Episodes
            .Where(e => e.SeasonId == seasonId)
            .OrderBy(e => e.EpisodeNumber)
            .ToListAsync();

        if (episodes.Count == 0)
            return ServiceResult<List<EpisodeDto>>.Fail("Season not found or has no episodes", 404);

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
                StillUrl = BuildTmdbImageUrl(ep.StillPath, "w300"),
                AirDate = ep.AirDate,
                Runtime = ep.Runtime,
                TmdbRating = ep.TmdbRating,
                State = ws?.State ?? WatchState.Unseen,
                IsManualOverride = ws?.IsManualOverride ?? false
            };
        }).ToList();

        return ServiceResult<List<EpisodeDto>>.Ok(dtos);
    }

    public async Task<ServiceResult<object>> RateSeriesAsync(int id, int profileId, UserRatingDto dto)
    {
        var ratingValidation = ValidateUserRating(dto.Rating);
        if (ratingValidation is not null) return ratingValidation;

        var series = await _context.Series.FirstOrDefaultAsync(s => s.Id == id);
        if (series is null) return ServiceResult<object>.Fail("Series not found", 404);

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
        return ServiceResult<object>.Ok(new { userRating = dto.Rating });
    }

    public async Task<ServiceResult<object>> RateEpisodeAsync(int seriesId, int episodeId, int profileId, UserRatingDto dto)
    {
        var ratingValidation = ValidateUserRating(dto.Rating);
        if (ratingValidation is not null) return ratingValidation;

        var episode = await _context.Episodes
            .Include(e => e.Season)
            .FirstOrDefaultAsync(e => e.Id == episodeId && e.Season.SeriesId == seriesId);

        if (episode is null) return ServiceResult<object>.Fail("Episode not found", 404);

        var series = await _context.Series.FirstOrDefaultAsync(s => s.Id == seriesId);
        if (series is null) return ServiceResult<object>.Fail("Series not found", 404);

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
        return ServiceResult<object>.Ok(new { userRating = dto.Rating });
    }

    public async Task<ServiceResult<object>> RateSeasonAsync(int seriesId, int seasonId, int profileId, UserRatingDto dto)
    {
        var ratingValidation = ValidateUserRating(dto.Rating);
        if (ratingValidation is not null) return ratingValidation;

        var season = await _context.Seasons
            .FirstOrDefaultAsync(s => s.Id == seasonId && s.SeriesId == seriesId);

        if (season is null) return ServiceResult<object>.Fail("Season not found", 404);

        var series = await _context.Series.FirstOrDefaultAsync(s => s.Id == seriesId);
        if (series is null) return ServiceResult<object>.Fail("Series not found", 404);

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
        return ServiceResult<object>.Ok(new { userRating = dto.Rating });
    }

    public async Task<ServiceResult<List<CastMemberDto>>> GetSeriesCreditsAsync(int id)
    {
        var series = await _context.Series
            .Include(s => s.MediaItem)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (series is null)
            return ServiceResult<List<CastMemberDto>>.Fail("Series not found", 404);

        if (!series.MediaItem.TmdbId.HasValue || !_tmdbClient.IsConfigured)
            return ServiceResult<List<CastMemberDto>>.Ok(new List<CastMemberDto>());

        var credits = await _tmdbClient.GetTvAggregateCreditsAsync(series.MediaItem.TmdbId.Value);
        if (credits?.Cast is null)
            return ServiceResult<List<CastMemberDto>>.Ok(new List<CastMemberDto>());

        var cast = credits.Cast
            .OrderBy(c => c.Order)
            .Take(20)
            .Select(c => new CastMemberDto
            {
                TmdbPersonId = c.Id,
                Name = c.Name ?? "",
                Character = c.Roles?.OrderByDescending(r => r.EpisodeCount).FirstOrDefault()?.Character,
                ProfilePath = c.ProfilePath,
                TotalEpisodeCount = c.TotalEpisodeCount,
            })
            .ToList();

        return ServiceResult<List<CastMemberDto>>.Ok(cast);
    }

    // ── Movies ──────────────────────────────────────────────────────────────

    public async Task<ServiceResult<PagedResult<MovieListDto>>> GetMoviesAsync(MediaQueryParameters query)
    {
        var profileId = query.ProfileId;

        var baseQuery = _context.Movies
            .Include(m => m.MediaItem)
            .AsQueryable();

        // Only show movies that this profile has synced or manually added
        if (profileId.HasValue)
        {
            baseQuery = baseQuery.Where(m =>
                m.WatchStates.Any(ws => ws.ProfileId == profileId.Value));

            // Exclude movies blocked by this profile
            baseQuery = baseQuery.Where(m =>
                !_context.ProfileMediaBlocks.Any(b => b.ProfileId == profileId.Value && b.MediaItemId == m.MediaItemId));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.ToLower();
            baseQuery = baseQuery.Where(m =>
                m.MediaItem.Title.ToLower().Contains(search) ||
                (m.MediaItem.OriginalTitle != null && m.MediaItem.OriginalTitle.ToLower().Contains(search)));
        }

        var projected = baseQuery.Select(m => new MovieListDto
        {
            Id = m.Id,
            MediaItemId = m.MediaItemId,
            Title = m.MediaItem.Title,
            PosterPath = m.MediaItem.PosterPath,
            Runtime = m.Runtime,
            ReleaseDate = m.MediaItem.ReleaseDate,
            State = profileId.HasValue
                ? (m.WatchStates.Where(ws => ws.ProfileId == profileId.Value && ws.MovieId == m.Id)
                    .Select(ws => ws.State).FirstOrDefault())
                : WatchState.Unseen,
            UserRating = profileId.HasValue
                ? m.WatchStates.Where(ws => ws.ProfileId == profileId.Value && ws.MovieId == m.Id)
                    .Select(ws => ws.UserRating).FirstOrDefault()
                : null,
            TmdbRating = m.MediaItem.ExternalRatings
                .Where(er => er.Provider == ExternalProvider.Tmdb)
                .Select(er => er.Score != null ? (double?)Convert.ToDouble(er.Score) : null)
                .FirstOrDefault()
        });

        if (!string.IsNullOrWhiteSpace(query.State) && Enum.TryParse<WatchState>(query.State, true, out var stateFilter))
        {
            projected = projected.Where(m => m.State == stateFilter);
        }

        int totalCount;
        List<MovieListDto> data;

        var sortKey = query.SortBy?.ToLower();
        if (sortKey == "top" || sortKey == "grade")
        {
            var allData = await projected.ToListAsync();
            totalCount = allData.Count;
            IEnumerable<MovieListDto> sorted = sortKey switch
            {
                "top" => query.SortDescending
                    ? allData
                        .OrderBy(m => m.UserRating.HasValue ? 0 : m.TmdbRating.HasValue ? 1 : 2)
                        .ThenByDescending(m => m.UserRating ?? 0)
                        .ThenByDescending(m => m.TmdbRating ?? 0)
                        .ThenByDescending(m => m.ReleaseDate)
                    : allData
                        .OrderByDescending(m => m.UserRating.HasValue ? 0 : m.TmdbRating.HasValue ? 1 : 2)
                        .ThenBy(m => m.UserRating ?? 0)
                        .ThenBy(m => m.TmdbRating ?? 0)
                        .ThenBy(m => m.ReleaseDate),
                _ => query.SortDescending // grade
                    ? allData
                        .OrderBy(m => m.UserRating.HasValue ? 0 : 1)
                        .ThenByDescending(m => m.UserRating ?? 0)
                    : allData
                        .OrderByDescending(m => m.UserRating.HasValue ? 0 : 1)
                        .ThenBy(m => m.UserRating ?? 0),
            };
            data = sorted.Skip(query.Skip).Take(query.Take).ToList();
        }
        else
        {
            projected = query.SortBy?.ToLower() switch
            {
                "title" => query.SortDescending ? projected.OrderByDescending(m => m.Title) : projected.OrderBy(m => m.Title),
                "release" => query.SortDescending ? projected.OrderByDescending(m => m.ReleaseDate) : projected.OrderBy(m => m.ReleaseDate),
                _ => projected.OrderBy(m => m.Title)
            };

            totalCount = await projected.CountAsync();
            data = await projected
                .Skip(query.Skip)
                .Take(query.Take)
                .ToListAsync();
        }

        return ServiceResult<PagedResult<MovieListDto>>.Ok(new PagedResult<MovieListDto>
        {
            Data = data,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        });
    }

    public async Task<ServiceResult<MovieDetailDto>> GetMovieDetailAsync(int id, int? profileId)
    {
        var movie = await _context.Movies
            .Include(m => m.MediaItem)
                .ThenInclude(mi => mi.ExternalRatings)
            .Include(m => m.MediaItem)
                .ThenInclude(mi => mi.Translations)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (movie is null)
            return ServiceResult<MovieDetailDto>.Fail("Movie not found", 404);

        var watchState = WatchState.Unseen;
        decimal? userRating = null;
        if (profileId.HasValue)
        {
            var ws = await _context.ProfileWatchStates
                .FirstOrDefaultAsync(w => w.ProfileId == profileId.Value && w.MovieId == movie.Id);
            if (ws is not null)
            {
                watchState = ws.State;
                userRating = ws.UserRating;
            }
        }

        DateTime? watchedAt = null;
        if (profileId.HasValue && watchState == WatchState.Seen)
        {
            watchedAt = await _context.WatchEvents
                .Where(e => e.ProfileId == profileId.Value && e.MovieId == movie.Id && e.EventType == WatchEventType.Finished)
                .OrderByDescending(e => e.Timestamp)
                .Select(e => (DateTime?)e.Timestamp)
                .FirstOrDefaultAsync();
        }

        var dto = new MovieDetailDto
        {
            Id = movie.Id,
            MediaItemId = movie.MediaItemId,
            Title = movie.MediaItem.Title,
            OriginalTitle = movie.MediaItem.OriginalTitle,
            Overview = movie.MediaItem.Overview,
            PosterPath = movie.MediaItem.PosterPath,
            BackdropPath = movie.MediaItem.BackdropPath,
            ReleaseDate = movie.MediaItem.ReleaseDate,
            OriginalLanguage = movie.MediaItem.OriginalLanguage,
            Runtime = movie.Runtime,
            Genres = movie.MediaItem.Genres,
            State = watchState,
            UserRating = userRating,
            WatchedAt = watchedAt,
            IsBlocked = profileId.HasValue && await _context.ProfileMediaBlocks
                .AnyAsync(b => b.ProfileId == profileId.Value && b.MediaItemId == movie.MediaItemId),
            Ratings = movie.MediaItem.ExternalRatings.Select(r => new ExternalRatingDto
            {
                Provider = r.Provider,
                Score = r.Score,
                VoteCount = r.VoteCount
            }).ToList(),
            SpanishTranslation = movie.MediaItem.Translations
                .Where(t => !string.IsNullOrWhiteSpace(t.Language)
                    && t.Language.StartsWith("es", StringComparison.OrdinalIgnoreCase))
                .Select(t => new TranslationDto
                {
                    Language = t.Language,
                    Title = t.Title,
                    Overview = t.Overview
                }).FirstOrDefault()
        };

        return ServiceResult<MovieDetailDto>.Ok(dto);
    }

    public async Task<ServiceResult<object>> RateMovieAsync(int id, int profileId, UserRatingDto dto)
    {
        var ratingValidation = ValidateUserRating(dto.Rating);
        if (ratingValidation is not null) return ratingValidation;

        var movie = await _context.Movies.FindAsync(id);
        if (movie is null) return ServiceResult<object>.Fail("Movie not found", 404);

        var ws = await _context.ProfileWatchStates
            .FirstOrDefaultAsync(w => w.ProfileId == profileId && w.MovieId == id);

        if (ws is null)
        {
            ws = new ProfileWatchState
            {
                ProfileId = profileId,
                MediaItemId = movie.MediaItemId,
                MovieId = id,
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
        return ServiceResult<object>.Ok(new { userRating = dto.Rating });
    }

    public async Task<ServiceResult<List<CastMemberDto>>> GetMovieCreditsAsync(int id)
    {
        var movie = await _context.Movies
            .Include(m => m.MediaItem)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (movie is null)
            return ServiceResult<List<CastMemberDto>>.Fail("Movie not found", 404);

        if (!movie.MediaItem.TmdbId.HasValue || !_tmdbClient.IsConfigured)
            return ServiceResult<List<CastMemberDto>>.Ok(new List<CastMemberDto>());

        var credits = await _tmdbClient.GetMovieCreditsAsync(movie.MediaItem.TmdbId.Value);
        if (credits?.Cast is null)
            return ServiceResult<List<CastMemberDto>>.Ok(new List<CastMemberDto>());

        var cast = credits.Cast
            .OrderBy(c => c.Order)
            .Take(20)
            .Select(c => new CastMemberDto
            {
                TmdbPersonId = c.Id,
                Name = c.Name ?? "",
                Character = c.Character,
                ProfilePath = c.ProfilePath,
            })
            .ToList();

        return ServiceResult<List<CastMemberDto>>.Ok(cast);
    }

    // ── Private helpers ─────────────────────────────────────────────────────

    private async Task<bool> IsSeriesIncludedByWatchlistAsync(int userId, int profileId, int mediaItemId)
    {
        var isInProfile = await _context.ProfileWatchStates
            .AnyAsync(ws => ws.ProfileId == profileId && ws.MediaItemId == mediaItemId);
        if (!isInProfile) return false;

        var isBlocked = await _context.ProfileMediaBlocks
            .AnyAsync(b => b.ProfileId == profileId && b.MediaItemId == mediaItemId);
        if (isBlocked) return false;

        var rootWatchlistIds = await _context.WatchlistMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.WatchlistId)
            .Distinct()
            .ToListAsync();

        var visited = new HashSet<int>();
        foreach (var watchlistId in rootWatchlistIds)
        {
            if (await WatchlistTreeContainsDashboardMediaAsync(watchlistId, mediaItemId, visited))
                return true;
        }

        return false;
    }

    private static ServiceResult<object>? ValidateUserRating(decimal? rating)
    {
        if (rating is null) return null;

        return rating < MinUserRating || rating > MaxUserRating
            ? ServiceResult<object>.Fail($"Rating must be between {MinUserRating} and {MaxUserRating}", 400)
            : null;
    }

    private async Task<bool> WatchlistTreeContainsDashboardMediaAsync(
        int watchlistId,
        int mediaItemId,
        HashSet<int> visited)
    {
        if (!visited.Add(watchlistId)) return false;

        var items = await _context.WatchlistItems
            .Where(i => i.WatchlistId == watchlistId && i.Status != WatchlistStatus.Dropped)
            .Select(i => new
            {
                i.ItemType,
                i.MediaItemId,
                i.ChildWatchlistId,
                MediaType = i.MediaItem != null ? (MediaType?)i.MediaItem.MediaType : null
            })
            .ToListAsync();

        foreach (var item in items)
        {
            if (item.ItemType == WatchlistItemType.MediaItem
                && item.MediaItemId == mediaItemId
                && item.MediaType == MediaType.Series)
            {
                return true;
            }

            if (item.ItemType == WatchlistItemType.Watchlist
                && item.ChildWatchlistId.HasValue
                && await WatchlistTreeContainsDashboardMediaAsync(item.ChildWatchlistId.Value, mediaItemId, visited))
            {
                return true;
            }
        }

        return false;
    }

    private static string? BuildTmdbImageUrl(string? path, string size)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return path;

        return $"https://image.tmdb.org/t/p/{size}{path}";
    }
}
