using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure;

namespace Jellywatch.Api.Services.Assets;

public class AssetCacheService : IAssetCacheService
{
    private readonly JellywatchDbContext _context;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AssetCacheService> _logger;
    private readonly string _assetDirectory;

    public AssetCacheService(
        JellywatchDbContext context,
        IHttpClientFactory httpClientFactory,
        ILogger<AssetCacheService> logger)
    {
        _context = context;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        // Store images relative to the app data directory
        var dataDir = Environment.GetEnvironmentVariable("APP_DATA_PATH") ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        _assetDirectory = Path.Combine(dataDir, "images");
        Directory.CreateDirectory(_assetDirectory);
    }

    public string GetAssetDirectory() => _assetDirectory;

    public async Task<string?> GetOrDownloadAsync(int mediaItemId, string imageType, string remoteUrl)
    {
        var dir = Path.Combine(_assetDirectory, mediaItemId.ToString());
        var fileName = $"{imageType}{GetExtension(remoteUrl)}";
        var localPath = Path.Combine(dir, fileName);

        // Return existing cached file
        if (File.Exists(localPath))
            return localPath;

        try
        {
            Directory.CreateDirectory(dir);

            var client = _httpClientFactory.CreateClient("AssetClient");
            var response = await client.GetAsync(remoteUrl);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync();
            await File.WriteAllBytesAsync(localPath, bytes);

            // Update the MediaImage record with local path
            var image = await _context.MediaImages
                .FirstOrDefaultAsync(i => i.MediaItemId == mediaItemId && i.RemoteUrl == remoteUrl);

            if (image is not null)
            {
                image.LocalPath = localPath;
                await _context.SaveChangesAsync();
            }

            _logger.LogDebug("Cached image for MediaItem {MediaItemId}: {ImageType} → {LocalPath}",
                mediaItemId, imageType, localPath);

            return localPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download image for MediaItem {MediaItemId} from {RemoteUrl}",
                mediaItemId, remoteUrl);
            return null;
        }
    }

    public Task<string?> GetLocalPathAsync(int mediaItemId, string imageType)
    {
        var dir = Path.Combine(_assetDirectory, mediaItemId.ToString());

        if (!Directory.Exists(dir))
            return Task.FromResult<string?>(null);

        // Custom upload takes priority over cached remote images
        var customFiles = Directory.GetFiles(dir, $"{imageType}-custom.*");
        if (customFiles.Length > 0) return Task.FromResult<string?>(customFiles[0]);

        var files = Directory.GetFiles(dir, $"{imageType}.*");
        return Task.FromResult(files.Length > 0 ? files[0] : null);
    }

    public async Task SelectImageAsync(int mediaItemId, string imageType, string remoteUrl)
    {
        var dir = Path.Combine(_assetDirectory, mediaItemId.ToString());

        // Delete all non-custom files of this image type from disk
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(dir, $"{imageType}.*"))
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                if (!stem.EndsWith("-custom", StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);
            }
        }

        // Reset LocalPath for all images of this type
        if (!Enum.TryParse<ImageType>(imageType, ignoreCase: true, out var imageTypeEnum))
            return;

        var images = await _context.MediaImages
            .Where(i => i.MediaItemId == mediaItemId
                && i.ImageType == imageTypeEnum
                && i.SeasonId == null && i.EpisodeId == null)
            .ToListAsync();

        foreach (var img in images)
            img.LocalPath = null;
        await _context.SaveChangesAsync();

        // Download the selected one
        await GetOrDownloadAsync(mediaItemId, imageType, remoteUrl);
    }

    public async Task RefreshAsync(int mediaItemId)
    {
        var dir = Path.Combine(_assetDirectory, mediaItemId.ToString());

        // Delete all non-custom cached files so stale images on disk never block fresh downloads
        if (Directory.Exists(dir))
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                var stem = Path.GetFileNameWithoutExtension(file);
                if (!stem.EndsWith("-custom", StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);
            }
        }

        var images = await _context.MediaImages
            .Where(i => i.MediaItemId == mediaItemId && i.RemoteUrl != null)
            .ToListAsync();

        foreach (var image in images)
        {
            var imageType = image.ImageType.ToString().ToLowerInvariant();
            if (image.RemoteUrl is not null)
            {
                image.LocalPath = null;
                await GetOrDownloadAsync(mediaItemId, imageType, image.RemoteUrl);
            }
        }
    }

    private static string GetExtension(string url)
    {
        try
        {
            var uri = new Uri(url);
            var ext = Path.GetExtension(uri.AbsolutePath);
            return string.IsNullOrWhiteSpace(ext) ? ".jpg" : ext;
        }
        catch
        {
            return ".jpg";
        }
    }
}
