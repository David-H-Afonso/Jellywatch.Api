using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Helpers;
using Jellywatch.Api.Infrastructure;
using Jellywatch.Api.Services.Metadata;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class AdminController : BaseApiController
{
    private readonly JellywatchDbContext _context;
    private readonly IMetadataResolutionService _metadataService;

    public AdminController(JellywatchDbContext context, IMetadataResolutionService metadataService)
    {
        _context = context;
        _metadataService = metadataService;
    }

    private async Task<bool> IsAdminAsync() =>
        (await _context.Users.FindAsync(CurrentUserId))?.IsAdmin == true;

    // ── Users ────────────────────────────────────────────────────────────────

    [HttpGet("users")]
    public async Task<ActionResult<List<UserDto>>> GetUsers()
    {
        if (!await IsAdminAsync()) return Forbid();

        var users = await _context.Users
            .OrderBy(u => u.Username)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Username = u.Username,
                JellyfinUserId = u.JellyfinUserId,
                IsAdmin = u.IsAdmin,
                AvatarUrl = u.AvatarUrl,
                PreferredLanguage = u.PreferredLanguage,
                CreatedAt = u.CreatedAt
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("profiles")]
    public async Task<ActionResult<List<ProfileDto>>> GetAllProfiles()
    {
        if (!await IsAdminAsync()) return Forbid();

        var profiles = await _context.Profiles
            .OrderBy(p => p.DisplayName)
            .Select(p => new ProfileDto
            {
                Id = p.Id,
                DisplayName = p.DisplayName,
                JellyfinUserId = p.JellyfinUserId,
                IsJoint = p.IsJoint,
                UserId = p.UserId,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();

        return Ok(profiles);
    }

    // ── Import Queue ─────────────────────────────────────────────────────────

    [HttpGet("import-queue")]
    public async Task<ActionResult<PagedResult<ImportQueueItemDto>>> GetImportQueue([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        if (!await IsAdminAsync()) return Forbid();

        var query = _context.ImportQueueItems.OrderByDescending(i => i.CreatedAt);
        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(i => new ImportQueueItemDto
            {
                Id = i.Id,
                JellyfinItemId = i.JellyfinItemId,
                MediaType = i.MediaType.ToString(),
                Priority = i.Priority,
                Status = i.Status.ToString(),
                RetryCount = i.RetryCount,
                CreatedAt = i.CreatedAt
            })
            .ToListAsync();

        return Ok(new PagedResult<ImportQueueItemDto>
        {
            Data = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    // ── Media Library ────────────────────────────────────────────────────────

    [HttpGet("media")]
    public async Task<ActionResult<PagedResult<MediaLibraryItemDto>>> GetMediaLibrary([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        if (!await IsAdminAsync()) return Forbid();

        var query = _context.MediaItems.OrderBy(m => m.Title);
        var totalCount = await query.CountAsync();

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MediaLibraryItemDto
            {
                Id = m.Id,
                Title = m.Title,
                MediaType = m.MediaType.ToString(),
                PosterPath = m.PosterPath,
                ReleaseDate = m.ReleaseDate,
                Status = m.Status,
                TmdbId = m.TmdbId,
                TvMazeId = m.TvMazeId,
                ImdbId = m.ImdbId,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();

        return Ok(new PagedResult<MediaLibraryItemDto>
        {
            Data = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        });
    }

    [HttpDelete("media/{id:int}")]
    public async Task<IActionResult> DeleteMediaItem(int id)
    {
        if (!await IsAdminAsync()) return Forbid();

        var mediaItem = await _context.MediaItems.FindAsync(id);
        if (mediaItem is null) return NotFound();

        // Clear MediaItemId from JellyfinLibraryItems so the series can be re-imported
        var libraryItems = await _context.JellyfinLibraryItems
            .Where(j => j.MediaItemId == id)
            .ToListAsync();

        foreach (var li in libraryItems)
            li.MediaItemId = null;

        _context.MediaItems.Remove(mediaItem);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("media/{id:int}/refresh")]
    public async Task<IActionResult> RefreshMediaItem(int id, [FromBody] RefreshMediaItemDto? dto = null)
    {
        if (!await IsAdminAsync()) return Forbid();

        var mediaItem = await _context.MediaItems.FindAsync(id);
        if (mediaItem is null) return NotFound();

        var refreshImages = dto?.RefreshImages ?? true;
        await _metadataService.RefreshMediaItemAsync(id, dto?.ForceTmdbId, refreshImages);
        return Ok(new { message = "Refresh complete", title = mediaItem.Title });
    }

    [HttpGet("media/{id:int}/poster-options")]
    public async Task<IActionResult> GetPosterOptions(int id)
    {
        if (!await IsAdminAsync()) return Forbid();

        var options = await _metadataService.GetPosterOptionsAsync(id);
        return Ok(options);
    }

    [HttpPost("media/{id:int}/select-poster")]
    public async Task<IActionResult> SelectPoster(int id, [FromBody] SelectPosterDto dto)
    {
        if (!await IsAdminAsync()) return Forbid();

        var mediaItem = await _context.MediaItems.FindAsync(id);
        if (mediaItem is null) return NotFound();

        await _metadataService.SelectPosterAsync(id, dto.RemoteUrl);
        return Ok(new { message = "Poster selected" });
    }

    [HttpGet("media/{id:int}/logo-options")]
    public async Task<IActionResult> GetLogoOptions(int id)
    {
        if (!await IsAdminAsync()) return Forbid();

        var options = await _metadataService.GetLogoOptionsAsync(id);
        return Ok(options);
    }

    [HttpPost("media/{id:int}/select-logo")]
    public async Task<IActionResult> SelectLogo(int id, [FromBody] SelectPosterDto dto)
    {
        if (!await IsAdminAsync()) return Forbid();

        var mediaItem = await _context.MediaItems.FindAsync(id);
        if (mediaItem is null) return NotFound();

        await _metadataService.SelectLogoAsync(id, dto.RemoteUrl);
        return Ok(new { message = "Logo selected" });
    }

    [HttpPost("media/refresh-all-metadata")]
    public async Task<IActionResult> RefreshAllMetadata()
    {
        if (!await IsAdminAsync()) return Forbid();

        var count = await _metadataService.RefreshAllMetadataAsync();
        return Ok(new { message = $"Refreshed metadata for {count} items", count });
    }

    [HttpPost("media/refresh-all-images")]
    public async Task<IActionResult> RefreshAllImages()
    {
        if (!await IsAdminAsync()) return Forbid();

        var count = await _metadataService.RefreshAllImagesAsync();
        return Ok(new { message = $"Refreshed images for {count} items", count });
    }

    // ── Blacklist ─────────────────────────────────────────────────────────────

    [HttpGet("blacklist")]
    public async Task<ActionResult<List<BlacklistedItemDto>>> GetBlacklist()
    {
        if (!await IsAdminAsync()) return Forbid();

        var items = await _context.BlacklistedItems
            .OrderByDescending(b => b.CreatedAt)
            .Select(b => new BlacklistedItemDto
            {
                Id = b.Id,
                JellyfinItemId = b.JellyfinItemId,
                DisplayName = b.DisplayName,
                Reason = b.Reason,
                CreatedAt = b.CreatedAt
            })
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("blacklist")]
    public async Task<ActionResult<BlacklistedItemDto>> AddToBlacklist([FromBody] AddToBlacklistDto dto)
    {
        if (!await IsAdminAsync()) return Forbid();

        var existing = await _context.BlacklistedItems
            .FirstOrDefaultAsync(b => b.JellyfinItemId == dto.JellyfinItemId);

        if (existing is not null)
            return Conflict(new { message = "Already blacklisted" });

        var item = new BlacklistedItem
        {
            JellyfinItemId = dto.JellyfinItemId,
            DisplayName = dto.DisplayName,
            Reason = dto.Reason
        };

        _context.BlacklistedItems.Add(item);

        // Also remove from import queue if pending
        var queueItems = await _context.ImportQueueItems
            .Where(q => q.JellyfinItemId == dto.JellyfinItemId)
            .ToListAsync();
        _context.ImportQueueItems.RemoveRange(queueItems);

        await _context.SaveChangesAsync();

        return Ok(new BlacklistedItemDto
        {
            Id = item.Id,
            JellyfinItemId = item.JellyfinItemId,
            DisplayName = item.DisplayName,
            Reason = item.Reason,
            CreatedAt = item.CreatedAt
        });
    }

    [HttpDelete("blacklist/{id:int}")]
    public async Task<IActionResult> RemoveFromBlacklist(int id)
    {
        if (!await IsAdminAsync()) return Forbid();

        var item = await _context.BlacklistedItems.FindAsync(id);
        if (item is null) return NotFound();

        _context.BlacklistedItems.Remove(item);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
