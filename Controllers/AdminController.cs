using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Common;
using Jellywatch.Api.Infrastructure.ExternalServices;
using Jellywatch.Api.Infrastructure.Persistence;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class AdminController : BaseApiController
{
    private readonly IAdminService _adminService;
    private readonly JellywatchDbContext _context;
    private readonly IJellyfinApiClient _jellyfinClient;

    public AdminController(IAdminService adminService, JellywatchDbContext context, IJellyfinApiClient jellyfinClient)
    {
        _adminService = adminService;
        _context = context;
        _jellyfinClient = jellyfinClient;
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<UserDto>>> GetUsers()
    {
        var result = await _adminService.GetUsersAsync(CurrentUserId);
        return ToActionResult(result);
    }

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var result = await _adminService.DeleteUserAsync(CurrentUserId, id);
        return ToActionResult(result);
    }

    [HttpGet("profiles")]
    public async Task<ActionResult<List<ProfileDto>>> GetAllProfiles()
    {
        var result = await _adminService.GetAllProfilesAsync(CurrentUserId);
        return ToActionResult(result);
    }

    [HttpGet("jellyfin-users")]
    public async Task<IActionResult> GetJellyfinUsers()
    {
        var result = await _adminService.GetJellyfinUsersAsync(CurrentUserId);
        return ToActionResult(result);
    }

    [HttpPost("add-profile")]
    public async Task<IActionResult> AddProfileFromJellyfin([FromBody] AddProfileRequest request)
    {
        var result = await _adminService.AddProfileFromJellyfinAsync(CurrentUserId, request);
        return ToActionResult(result);
    }

    [HttpGet("import-queue")]
    public async Task<ActionResult<PagedResult<ImportQueueItemDto>>> GetImportQueue([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _adminService.GetImportQueueAsync(CurrentUserId, page, pageSize);
        return ToActionResult(result);
    }

    [HttpGet("media")]
    public async Task<ActionResult<PagedResult<MediaLibraryItemDto>>> GetMediaLibrary([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        var result = await _adminService.GetMediaLibraryAsync(CurrentUserId, page, pageSize);
        return ToActionResult(result);
    }

    [HttpDelete("media/{id:int}")]
    public async Task<IActionResult> DeleteMediaItem(int id)
    {
        var result = await _adminService.DeleteMediaItemAsync(CurrentUserId, id);
        return ToActionResult(result);
    }

    [HttpPost("media/{id:int}/refresh")]
    public async Task<IActionResult> RefreshMediaItem(int id, [FromBody] RefreshMediaItemDto? dto = null)
    {
        var result = await _adminService.RefreshMediaItemAsync(CurrentUserId, id, dto);
        return ToActionResult(result);
    }

    [HttpGet("media/{id:int}/poster-options")]
    public async Task<IActionResult> GetPosterOptions(int id)
    {
        var result = await _adminService.GetPosterOptionsAsync(CurrentUserId, id);
        return ToActionResult(result);
    }

    [HttpPost("media/{id:int}/select-poster")]
    public async Task<IActionResult> SelectPoster(int id, [FromBody] SelectPosterDto dto)
    {
        var result = await _adminService.SelectPosterAsync(CurrentUserId, id, dto);
        return ToActionResult(result);
    }

    [HttpGet("media/{id:int}/logo-options")]
    public async Task<IActionResult> GetLogoOptions(int id)
    {
        var result = await _adminService.GetLogoOptionsAsync(CurrentUserId, id);
        return ToActionResult(result);
    }

    [HttpPost("media/{id:int}/select-logo")]
    public async Task<IActionResult> SelectLogo(int id, [FromBody] SelectPosterDto dto)
    {
        var result = await _adminService.SelectLogoAsync(CurrentUserId, id, dto);
        return ToActionResult(result);
    }

    [HttpPost("media/refresh-all-metadata")]
    public async Task<IActionResult> RefreshAllMetadata()
    {
        var result = await _adminService.RefreshAllMetadataAsync(CurrentUserId);
        return ToActionResult(result);
    }

    [HttpPost("media/refresh-all-images")]
    public async Task<IActionResult> RefreshAllImages()
    {
        var result = await _adminService.RefreshAllImagesAsync(CurrentUserId);
        return ToActionResult(result);
    }

    [HttpDelete("profiles/{profileId:int}/media")]
    public async Task<IActionResult> PurgeProfileMedia(int profileId)
    {
        var result = await _adminService.PurgeProfileMediaAsync(CurrentUserId, profileId);
        return ToActionResult(result);
    }

    [HttpDelete("profiles/{id:int}")]
    public async Task<IActionResult> DeleteProfile(int id)
    {
        var result = await _adminService.DeleteProfileAsync(CurrentUserId, id);
        return ToActionResult(result);
    }

    [HttpPost("profiles/{id:int}/create-user")]
    public async Task<IActionResult> CreateUserForProfile(int id)
    {
        var result = await _adminService.CreateUserForProfileAsync(CurrentUserId, id);
        return ToActionResult(result);
    }

    [HttpGet("blacklist")]
    public async Task<ActionResult<List<BlacklistedItemDto>>> GetBlacklist()
    {
        var result = await _adminService.GetBlacklistAsync(CurrentUserId);
        return ToActionResult(result);
    }

    [HttpPost("blacklist")]
    public async Task<ActionResult<BlacklistedItemDto>> AddToBlacklist([FromBody] AddToBlacklistDto dto)
    {
        var result = await _adminService.AddToBlacklistAsync(CurrentUserId, dto);
        return ToActionResult(result);
    }

    [HttpDelete("blacklist/{id:int}")]
    public async Task<IActionResult> RemoveFromBlacklist(int id)
    {
        var result = await _adminService.RemoveFromBlacklistAsync(CurrentUserId, id);
        return ToActionResult(result);
    }

    [HttpGet("profile-blocks")]
    public async Task<IActionResult> GetAllProfileBlocks()
    {
        var result = await _adminService.GetAllProfileBlocksAsync(CurrentUserId);
        return ToActionResult(result);
    }

    [HttpPost("repair-jellyfin-ids")]
    public async Task<IActionResult> RepairJellyfinIds()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var user = await _context.Users.FindAsync(userId.Value);
        if (user?.IsAdmin != true) return Forbid();

        // Find all jellyfin_library_item entries with non-GUID IDs
        var allItems = await _context.JellyfinLibraryItems
            .Include(j => j.MediaItem)
            .ToListAsync();

        var syntheticItems = allItems.Where(j => !Guid.TryParse(j.JellyfinItemId, out _)).ToList();
        if (syntheticItems.Count == 0)
            return Ok(new { message = "No synthetic IDs found", repaired = 0, deleted = 0 });

        // Get a Jellyfin user to query the library
        var jellyfinUserId = await _context.Profiles
            .Where(p => p.JellyfinUserId != null && p.JellyfinUserId != "")
            .Select(p => p.JellyfinUserId)
            .FirstOrDefaultAsync();

        if (jellyfinUserId is null)
            return BadRequest(new { message = "No Jellyfin user configured" });

        // Query all Jellyfin items
        var jellyfinItems = await _jellyfinClient.GetItemsAsync(jellyfinUserId, itemTypes: "Movie,Series");

        // Build lookup maps by provider IDs
        var tmdbMap = new Dictionary<(int tmdbId, string type), string>();
        var imdbMap = new Dictionary<string, string>();
        foreach (var jfItem in jellyfinItems)
        {
            if (jfItem.ProviderIds is null) continue;
            var jfType = jfItem.Type == "Movie" ? "Movie" : "Series";
            if (jfItem.ProviderIds.TryGetValue("Tmdb", out var tmdbStr) && int.TryParse(tmdbStr, out var tmdbId))
                tmdbMap.TryAdd((tmdbId, jfType), jfItem.Id);
            if (jfItem.ProviderIds.TryGetValue("Imdb", out var imdbId) && !string.IsNullOrEmpty(imdbId))
                imdbMap.TryAdd(imdbId, jfItem.Id);
        }

        int repaired = 0, deleted = 0;
        var details = new List<object>();

        foreach (var item in syntheticItems)
        {
            string? realId = null;
            var mediaType = item.MediaItem?.MediaType == Domain.Enums.MediaType.Movie ? "Movie" : "Series";

            if (item.MediaItem?.TmdbId is int tmdb && tmdbMap.TryGetValue((tmdb, mediaType), out var byTmdb))
                realId = byTmdb;
            else if (!string.IsNullOrEmpty(item.MediaItem?.ImdbId) && imdbMap.TryGetValue(item.MediaItem.ImdbId, out var byImdb))
                realId = byImdb;

            if (realId is not null)
            {
                item.JellyfinItemId = realId;
                repaired++;
                details.Add(new { item.MediaItem?.Title, oldId = item.JellyfinItemId, newId = realId, action = "repaired" });
            }
            else
            {
                _context.JellyfinLibraryItems.Remove(item);
                deleted++;
                details.Add(new { item.MediaItem?.Title, oldId = item.JellyfinItemId, action = "deleted (not in Jellyfin)" });
            }
        }

        await _context.SaveChangesAsync();
        return Ok(new { message = $"Processed {syntheticItems.Count} synthetic IDs", repaired, deleted, details });
    }
}
