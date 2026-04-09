using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure;
using Jellywatch.Api.Services.Assets;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class AssetController : BaseApiController
{
    private readonly JellywatchDbContext _context;
    private readonly IAssetCacheService _assetService;
    private readonly ILogger<AssetController> _logger;

    public AssetController(
        JellywatchDbContext context,
        IAssetCacheService assetService,
        ILogger<AssetController> logger)
    {
        _context = context;
        _assetService = assetService;
        _logger = logger;
    }

    [HttpGet("{mediaItemId:int}/{imageType}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetImage(int mediaItemId, string imageType)
    {
        // Normalize to lowercase so file-system lookups are case-consistent on Linux
        imageType = imageType.ToLowerInvariant();

        // Try local cache first
        var localPath = await _assetService.GetLocalPathAsync(mediaItemId, imageType);
        if (localPath is not null && System.IO.File.Exists(localPath))
        {
            Response.Headers.CacheControl = "no-cache, must-revalidate";
            var contentType = GetContentType(localPath);
            return PhysicalFile(localPath, contentType);
        }

        // Find the remote URL in the database
        if (!Enum.TryParse<ImageType>(imageType, ignoreCase: true, out var imageTypeEnum))
            return NotFound(new { message = "Image not found" });

        var image = await _context.MediaImages
            .Where(i => i.MediaItemId == mediaItemId
                && i.ImageType == imageTypeEnum
                && i.SeasonId == null && i.EpisodeId == null
                && i.RemoteUrl != null)
            .OrderByDescending(i => i.LocalPath != null) // Prefer previously selected image
            .ThenBy(i => i.Language == "en" ? 0 : i.Language == "es" ? 1 : 2)
            .ThenBy(i => i.Id)
            .FirstOrDefaultAsync();

        if (image?.RemoteUrl is null)
            return NotFound(new { message = "Image not found" });

        // Download and cache
        var cached = await _assetService.GetOrDownloadAsync(mediaItemId, imageType, image.RemoteUrl);
        if (cached is not null && System.IO.File.Exists(cached))
        {
            Response.Headers.CacheControl = "no-cache, must-revalidate";
            var contentType = GetContentType(cached);
            return PhysicalFile(cached, contentType);
        }

        // Fallback: redirect to remote URL
        return Redirect(image.RemoteUrl);
    }

    [HttpPost("refresh/{mediaItemId:int}")]
    public async Task<IActionResult> RefreshImages(int mediaItemId)
    {
        var mediaItem = await _context.MediaItems.FindAsync(mediaItemId);
        if (mediaItem is null)
            return NotFound(new { message = "Media item not found" });

        await _assetService.RefreshAsync(mediaItemId);
        return Ok(new { message = "Image cache refreshed" });
    }

    [HttpPost("custom/{mediaItemId:int}/poster")]
    public async Task<IActionResult> UploadCustomPoster(int mediaItemId, IFormFile file)
    {
        var mediaItem = await _context.MediaItems.FindAsync(mediaItemId);
        if (mediaItem is null) return NotFound(new { message = "Media item not found" });

        var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext))
            return BadRequest(new { message = "Only jpg, jpeg, png, webp images are allowed" });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { message = "File is too large (max 10 MB)" });

        var dir = Path.Combine(_assetService.GetAssetDirectory(), mediaItemId.ToString());
        Directory.CreateDirectory(dir);

        // Remove any existing custom poster
        foreach (var existing in Directory.GetFiles(dir, "Poster-custom.*"))
            System.IO.File.Delete(existing);

        var localPath = Path.Combine(dir, $"Poster-custom{ext}");
        await using var stream = new FileStream(localPath, FileMode.Create);
        await file.CopyToAsync(stream);

        return Ok(new { message = "Custom poster uploaded" });
    }

    private static string GetContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }
}
