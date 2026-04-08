using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Helpers;
using Jellywatch.Api.Infrastructure;

namespace Jellywatch.Api.Controllers;

[Route("api/media/movies")]
public class MoviesController : BaseApiController
{
    private readonly JellywatchDbContext _context;

    public MoviesController(JellywatchDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<MovieListDto>>> GetMovies([FromQuery] MediaQueryParameters query)
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
                : null
        });

        if (!string.IsNullOrWhiteSpace(query.State) && Enum.TryParse<WatchState>(query.State, true, out var stateFilter))
        {
            projected = projected.Where(m => m.State == stateFilter);
        }

        projected = query.SortBy?.ToLower() switch
        {
            "title" => query.SortDescending ? projected.OrderByDescending(m => m.Title) : projected.OrderBy(m => m.Title),
            "release" => query.SortDescending ? projected.OrderByDescending(m => m.ReleaseDate) : projected.OrderBy(m => m.ReleaseDate),
            "grade" => query.SortDescending ? projected.OrderByDescending(m => m.UserRating) : projected.OrderBy(m => m.UserRating),
            _ => projected.OrderBy(m => m.Title)
        };

        var totalCount = await projected.CountAsync();
        var data = await projected
            .Skip(query.Skip)
            .Take(query.Take)
            .ToListAsync();

        return Ok(new PagedResult<MovieListDto>
        {
            Data = data,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        });
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MovieDetailDto>> GetMovieDetail(int id, [FromQuery] int? profileId)
    {
        var movie = await _context.Movies
            .Include(m => m.MediaItem)
                .ThenInclude(mi => mi.ExternalRatings)
            .Include(m => m.MediaItem)
                .ThenInclude(mi => mi.Translations)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (movie is null)
            return NotFound(new { message = "Movie not found" });

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

    [HttpPatch("{id:int}/rating")]
    public async Task<IActionResult> RateMovie(int id, [FromQuery] int profileId, [FromBody] UserRatingDto dto)
    {
        var movie = await _context.Movies.FindAsync(id);
        if (movie is null) return NotFound(new { message = "Movie not found" });

        var ws = await _context.ProfileWatchStates
            .FirstOrDefaultAsync(w => w.ProfileId == profileId && w.MovieId == id);

        if (ws is null)
        {
            ws = new Domain.ProfileWatchState
            {
                ProfileId = profileId,
                MediaItemId = movie.MediaItemId,
                MovieId = id,
                State = Domain.Enums.WatchState.Unseen,
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
