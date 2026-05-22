using Microsoft.AspNetCore.Mvc;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Common;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class AdminController : BaseApiController
{
    private readonly IAdminService _adminService;

    public AdminController(IAdminService adminService)
    {
        _adminService = adminService;
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
}
