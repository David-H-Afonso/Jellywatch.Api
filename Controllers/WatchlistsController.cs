using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Application;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Infrastructure.Persistence;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class WatchlistsController : BaseApiController
{
    private readonly IWatchlistService _watchlistService;
    private readonly JellywatchDbContext _context;
    private readonly IAssetCacheService _assetService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IJellyfinPlaylistSyncService _playlistSyncService;

    public WatchlistsController(IWatchlistService watchlistService, JellywatchDbContext context, IAssetCacheService assetService, IHttpClientFactory httpClientFactory, IJellyfinPlaylistSyncService playlistSyncService)
    {
        _watchlistService = watchlistService;
        _context = context;
        _assetService = assetService;
        _httpClientFactory = httpClientFactory;
        _playlistSyncService = playlistSyncService;
    }

    [HttpGet]
    public async Task<ActionResult<WatchlistIndexDto>> GetWatchlists([FromQuery] int? profileId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.GetWatchlistsAsync(userId.Value, profileId);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<WatchlistUserOptionDto>>> GetUserOptions([FromQuery] string? search, [FromQuery] int? watchlistId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.GetUserOptionsAsync(userId.Value, search, watchlistId);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WatchlistDetailDto>> GetWatchlist(int id, [FromQuery] int? profileId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.GetWatchlistAsync(userId.Value, id, profileId);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return Ok(result.Data);
    }

    [HttpPost]
    public async Task<ActionResult<WatchlistDetailDto>> CreateWatchlist([FromBody] CreateWatchlistDto dto, [FromQuery] int? profileId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.CreateWatchlistAsync(userId.Value, dto, profileId);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return CreatedAtAction(nameof(GetWatchlist), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateWatchlist(int id, [FromBody] UpdateWatchlistDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.UpdateWatchlistAsync(userId.Value, id, dto);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteWatchlist(int id)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.DeleteWatchlistAsync(userId.Value, id);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpDelete("{id:int}/me")]
    public async Task<IActionResult> LeaveWatchlist(int id)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.LeaveWatchlistAsync(userId.Value, id);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return NoContent();
    }

    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> CompleteWatchlist(int id)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.CompleteWatchlistAsync(userId.Value, id);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpPost("{id:int}/items")]
    public async Task<IActionResult> AddItem(int id, [FromBody] AddWatchlistItemDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.AddItemAsync(userId.Value, id, dto);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpPut("{id:int}/items/{itemId:int}")]
    public async Task<IActionResult> UpdateItem(int id, int itemId, [FromBody] UpdateWatchlistItemDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.UpdateItemAsync(userId.Value, id, itemId, dto);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpDelete("{id:int}/items/{itemId:int}")]
    public async Task<IActionResult> DeleteItem(int id, int itemId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.DeleteItemAsync(userId.Value, id, itemId);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpPut("{id:int}/items/reorder")]
    public async Task<IActionResult> ReorderItems(int id, [FromBody] ReorderWatchlistItemsDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.ReorderItemsAsync(userId.Value, id, dto);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpPost("{id:int}/members/invite")]
    public async Task<ActionResult<WatchlistInvitationDto>> InviteMember(int id, [FromBody] InviteWatchlistMemberDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.InviteMemberAsync(userId.Value, id, dto);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return Ok(result.Data);
    }

    [HttpPost("invitations/{invitationId:int}/accept")]
    public async Task<IActionResult> AcceptInvitation(int invitationId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.AcceptInvitationAsync(userId.Value, invitationId);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return NoContent();
    }

    [HttpPost("invitations/{invitationId:int}/reject")]
    public async Task<IActionResult> RejectInvitation(int invitationId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.RejectInvitationAsync(userId.Value, invitationId);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return NoContent();
    }

    [HttpPut("{id:int}/members/{memberId:int}")]
    public async Task<IActionResult> UpdateMember(int id, int memberId, [FromBody] UpdateWatchlistMemberDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.UpdateMemberAsync(userId.Value, id, memberId, dto);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpDelete("{id:int}/members/{memberId:int}")]
    public async Task<IActionResult> RemoveMember(int id, int memberId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.RemoveMemberAsync(userId.Value, id, memberId);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpPost("{id:int}/access-requests")]
    public async Task<IActionResult> RequestAccess(int id, [FromBody] CreateWatchlistAccessRequestDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.RequestAccessAsync(userId.Value, id, dto);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return NoContent();
    }

    [HttpPost("access-requests/{requestId:int}/approve")]
    public async Task<IActionResult> ApproveAccessRequest(int requestId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.ApproveAccessRequestAsync(userId.Value, requestId);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpPost("access-requests/{requestId:int}/reject")]
    public async Task<IActionResult> RejectAccessRequest(int requestId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.RejectAccessRequestAsync(userId.Value, requestId);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpPut("me/default")]
    public async Task<IActionResult> SetDefaultWatchlist([FromBody] SetDefaultWatchlistDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.SetDefaultWatchlistAsync(userId.Value, dto);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpGet("{id:int}/export")]
    public async Task<ActionResult<WatchlistExportDto>> ExportWatchlist(int id)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.ExportWatchlistAsync(userId.Value, id);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return Ok(result.Data);
    }

    [HttpPost("import")]
    public async Task<ActionResult<WatchlistImportResultDto>> ImportWatchlist([FromBody] WatchlistImportDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.ImportWatchlistAsync(userId.Value, dto);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("{id:int}/cover")]
    [AllowAnonymous]
    public async Task<IActionResult> GetCover(int id)
    {
        var watchlist = await _context.Watchlists
            .Include(w => w.Items.OrderBy(i => i.Position))
                .ThenInclude(i => i.MediaItem)
            .FirstOrDefaultAsync(w => w.Id == id);

        if (watchlist is null)
            return NotFound();

        // 1. Custom cover image
        if (!string.IsNullOrEmpty(watchlist.CoverImagePath) && System.IO.File.Exists(watchlist.CoverImagePath))
        {
            Response.Headers.CacheControl = "no-cache, must-revalidate";
            return PhysicalFile(watchlist.CoverImagePath, GetContentType(watchlist.CoverImagePath));
        }

        // 2. Fallback to first item's poster
        var firstMedia = watchlist.Items
            .Where(i => i.MediaItem != null)
            .OrderBy(i => i.Position)
            .Select(i => i.MediaItem!)
            .FirstOrDefault();

        if (firstMedia is null)
            return NotFound();

        // Try local asset cache
        var localPath = await _assetService.GetLocalPathAsync(firstMedia.Id, "poster");
        if (localPath is not null && System.IO.File.Exists(localPath))
        {
            Response.Headers.CacheControl = "no-cache, must-revalidate";
            return PhysicalFile(localPath, GetContentType(localPath));
        }

        // Redirect to standard asset endpoint
        return RedirectToAction("GetImage", "Asset", new { mediaItemId = firstMedia.Id, imageType = "poster" });
    }

    [HttpPost("{id:int}/cover")]
    [RequestSizeLimit(10_485_760)] // 10 MB — enforce at Kestrel level before buffering
    public async Task<IActionResult> UploadCover(int id, IFormFile file)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var member = await _context.WatchlistMembers
            .FirstOrDefaultAsync(m => m.WatchlistId == id && m.UserId == userId.Value
                && (m.Role == Domain.Enums.WatchlistRole.Owner || m.Role == Domain.Enums.WatchlistRole.Admin || m.CanUpdateWatchlist));
        if (member is null)
            return Forbid();

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext))
            return BadRequest(new { message = "Only jpg, jpeg, png, webp images are allowed" });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { message = "File is too large (max 10 MB)" });

        // Validate file content is a real image (magic bytes)
        var header = new byte[12];
        await using (var headerStream = file.OpenReadStream())
        {
            _ = await headerStream.ReadAsync(header);
        }
        if (!IsValidImageHeader(header))
            return BadRequest(new { message = "File content does not match a valid image format" });

        var dir = Path.Combine(_assetService.GetAssetDirectory(), "watchlists");
        Directory.CreateDirectory(dir);

        // Remove old cover
        var watchlist = await _context.Watchlists.FindAsync(id);
        if (watchlist is null) return NotFound();

        if (!string.IsNullOrEmpty(watchlist.CoverImagePath) && System.IO.File.Exists(watchlist.CoverImagePath))
            System.IO.File.Delete(watchlist.CoverImagePath);

        var localPath = Path.Combine(dir, $"{id}-cover{ext}");
        await using var stream = new FileStream(localPath, FileMode.Create);
        await file.CopyToAsync(stream);

        watchlist.CoverImagePath = localPath;
        watchlist.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Cover uploaded" });
    }

    [HttpPut("{id:int}/cover")]
    public async Task<IActionResult> SetCoverUrl(int id, [FromBody] SetWatchlistCoverDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var member = await _context.WatchlistMembers
            .FirstOrDefaultAsync(m => m.WatchlistId == id && m.UserId == userId.Value
                && (m.Role == Domain.Enums.WatchlistRole.Owner || m.Role == Domain.Enums.WatchlistRole.Admin || m.CanUpdateWatchlist));
        if (member is null)
            return Forbid();

        // Validate URL to prevent SSRF attacks
        var (isSafe, urlError) = await ValidateUrlAsync(dto.Url);
        if (!isSafe)
            return BadRequest(new { message = urlError });

        var watchlist = await _context.Watchlists.FindAsync(id);
        if (watchlist is null) return NotFound();

        // Download the image with size limit
        const long maxDownloadSize = 10 * 1024 * 1024; // 10 MB
        var httpClient = _httpClientFactory.CreateClient("CoverClient");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(dto.Url, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (TaskCanceledException)
        {
            return BadRequest(new { message = "Request to download image timed out" });
        }
        catch (HttpRequestException)
        {
            return BadRequest(new { message = "Could not download image from URL" });
        }

        using var _ = response; // Ensure disposal on all code paths

        if (!response.IsSuccessStatusCode)
        {
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
                return BadRequest(new { message = "The image URL redirects elsewhere. Please provide the direct image URL." });
            return BadRequest(new { message = "Could not download image from URL" });
        }

        // Reject early if Content-Length exceeds limit
        if (response.Content.Headers.ContentLength > maxDownloadSize)
            return BadRequest(new { message = "Remote image is too large (max 10 MB)" });

        // Read content with enforced size limit (Content-Length can be spoofed/absent)
        byte[] imageBytes;
        await using (var responseStream = await response.Content.ReadAsStreamAsync())
        {
            using var memoryStream = new MemoryStream();
            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await responseStream.ReadAsync(buffer)) > 0)
            {
                totalRead += bytesRead;
                if (totalRead > maxDownloadSize)
                    return BadRequest(new { message = "Remote image is too large (max 10 MB)" });
                memoryStream.Write(buffer, 0, bytesRead);
            }
            imageBytes = memoryStream.ToArray();
        }

        // Validate content is a real image via magic bytes and determine extension
        var ext = GetExtensionFromMagicBytes(imageBytes);
        if (ext is null)
            return BadRequest(new { message = "URL does not point to a valid image (jpg, png, webp)" });

        var dir = Path.Combine(_assetService.GetAssetDirectory(), "watchlists");
        Directory.CreateDirectory(dir);

        if (!string.IsNullOrEmpty(watchlist.CoverImagePath) && System.IO.File.Exists(watchlist.CoverImagePath))
            System.IO.File.Delete(watchlist.CoverImagePath);

        var localPath = Path.Combine(dir, $"{id}-cover{ext}");
        await System.IO.File.WriteAllBytesAsync(localPath, imageBytes);

        watchlist.CoverImagePath = localPath;
        watchlist.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Cover set from URL" });
    }

    [HttpDelete("{id:int}/cover")]
    public async Task<IActionResult> DeleteCover(int id)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var member = await _context.WatchlistMembers
            .FirstOrDefaultAsync(m => m.WatchlistId == id && m.UserId == userId.Value
                && (m.Role == Domain.Enums.WatchlistRole.Owner || m.Role == Domain.Enums.WatchlistRole.Admin || m.CanUpdateWatchlist));
        if (member is null)
            return Forbid();

        var watchlist = await _context.Watchlists.FindAsync(id);
        if (watchlist is null) return NotFound();

        if (!string.IsNullOrEmpty(watchlist.CoverImagePath) && System.IO.File.Exists(watchlist.CoverImagePath))
            System.IO.File.Delete(watchlist.CoverImagePath);

        watchlist.CoverImagePath = null;
        watchlist.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // ━━━━ JELLYFIN PLAYLIST SYNC ━━━━

    [HttpPost("{id}/jellyfin-sync/preview")]
    public async Task<IActionResult> PreviewPlaylistSync(int id)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _playlistSyncService.PreviewSyncAsync(id);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return Ok(result.Data);
    }

    [HttpPost("{id}/jellyfin-sync")]
    public async Task<IActionResult> CreatePlaylistSync(int id, [FromBody] CreatePlaylistSyncRequest request)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _playlistSyncService.CreatePlaylistFromWatchlistAsync(id, request.JellyfinUserId);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return Ok(new { message = "Playlist created and synced" });
    }

    [HttpPost("{id}/jellyfin-sync/resync")]
    public async Task<IActionResult> ResyncPlaylist(int id)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _playlistSyncService.SyncPlaylistAsync(id);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return Ok(new { message = "Playlist resynced" });
    }

    [HttpDelete("{id}/jellyfin-sync")]
    public async Task<IActionResult> UnlinkPlaylist(int id)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _playlistSyncService.UnlinkPlaylistAsync(id);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return NoContent();
    }

    // ━━━━ IMAGE SECURITY HELPERS ━━━━

    /// <summary>Validates a URL is safe to fetch (blocks SSRF to internal/private IPs).</summary>
    private static async Task<(bool IsSafe, string? Error)> ValidateUrlAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return (false, "Invalid URL format");

        if (uri.Scheme != "http" && uri.Scheme != "https")
            return (false, "Only http and https URLs are supported");

        var host = uri.DnsSafeHost;

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "metadata.google.internal", StringComparison.OrdinalIgnoreCase))
            return (false, "URLs pointing to internal services are not allowed");

        // If host is a raw IP, validate directly
        if (IPAddress.TryParse(host, out var ip))
        {
            if (IsPrivateOrReservedIP(ip))
                return (false, "URLs pointing to private or reserved IP addresses are not allowed");
            return (true, null);
        }

        // Resolve hostname and validate all resolved IPs
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host);
            if (addresses.Length == 0)
                return (false, "Could not resolve hostname");

            foreach (var addr in addresses)
            {
                if (IsPrivateOrReservedIP(addr))
                    return (false, "URLs pointing to private or reserved IP addresses are not allowed");
            }
        }
        catch
        {
            return (false, "Could not resolve hostname");
        }

        return (true, null);
    }

    private static bool IsPrivateOrReservedIP(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return bytes[0] == 10 ||                                         // 10.0.0.0/8
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || // 172.16.0.0/12
                   (bytes[0] == 192 && bytes[1] == 168) ||                  // 192.168.0.0/16
                   (bytes[0] == 169 && bytes[1] == 254) ||                  // 169.254.0.0/16 (link-local/cloud metadata)
                   bytes[0] == 127 ||                                        // 127.0.0.0/8
                   bytes[0] == 0;                                            // 0.0.0.0/8
        }

        return false;
    }

    /// <summary>Validates file header bytes match a known image format (JPEG, PNG, WebP).</summary>
    private static bool IsValidImageHeader(ReadOnlySpan<byte> header)
    {
        return GetExtensionFromMagicBytes(header) is not null;
    }

    /// <summary>Returns the file extension matching the magic bytes, or null if unrecognized.</summary>
    private static string? GetExtensionFromMagicBytes(ReadOnlySpan<byte> data)
    {
        // JPEG: FF D8 FF
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return ".jpg";

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (data.Length >= 8 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
            data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            return ".png";

        // WebP: RIFF....WEBP
        if (data.Length >= 12 &&
            data[0] == (byte)'R' && data[1] == (byte)'I' && data[2] == (byte)'F' && data[3] == (byte)'F' &&
            data[8] == (byte)'W' && data[9] == (byte)'E' && data[10] == (byte)'B' && data[11] == (byte)'P')
            return ".webp";

        return null;
    }

    private static string GetContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }
}
