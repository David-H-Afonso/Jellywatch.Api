using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure;

namespace Jellywatch.Api.Services.Metadata;

public class ImportQueueWorker : BackgroundService
{
    private const int ProcessingIntervalSeconds = 5;
    private const int MaxConcurrency = 1;
    private const int MaxRetries = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ImportQueueWorker> _logger;
    private readonly SemaphoreSlim _concurrencyLimiter = new(MaxConcurrency, MaxConcurrency);

    public ImportQueueWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ImportQueueWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Import Queue Worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingItemsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in import queue processing loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(ProcessingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Import Queue Worker stopped");
    }

    private async Task ProcessPendingItemsAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<JellywatchDbContext>();

        // Recover items stuck in Processing (e.g. from a previous crash)
        var stuckThreshold = DateTime.UtcNow.AddMinutes(-5);
        var stuckItems = await context.ImportQueueItems
            .Where(i => i.Status == ImportStatus.Processing && i.CreatedAt < stuckThreshold)
            .ToListAsync(stoppingToken);

        if (stuckItems.Count > 0)
        {
            foreach (var stuck in stuckItems)
            {
                stuck.Status = ImportStatus.Pending;
                stuck.RetryCount++;
            }
            await context.SaveChangesAsync(stoppingToken);
            _logger.LogWarning("Reset {Count} stuck import queue items back to Pending", stuckItems.Count);
        }

        var pendingItems = await context.ImportQueueItems
            .Where(i => i.Status == ImportStatus.Pending && (i.NextRetryAt == null || i.NextRetryAt <= DateTime.UtcNow))
            .OrderBy(i => i.Priority)
            .ThenBy(i => i.CreatedAt)
            .Take(MaxConcurrency * 2)
            .ToListAsync(stoppingToken);

        if (pendingItems.Count == 0) return;

        _logger.LogDebug("Processing {Count} items from import queue", pendingItems.Count);

        var tasks = pendingItems.Select(item => ProcessItemAsync(item.Id, stoppingToken));
        await Task.WhenAll(tasks);
    }

    private async Task ProcessItemAsync(int itemId, CancellationToken stoppingToken)
    {
        await _concurrencyLimiter.WaitAsync(stoppingToken);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<JellywatchDbContext>();
            var metadataService = scope.ServiceProvider.GetRequiredService<IMetadataResolutionService>();

            var item = await context.ImportQueueItems.FindAsync(new object[] { itemId }, stoppingToken);
            if (item is null || item.Status != ImportStatus.Pending) return;

            item.Status = ImportStatus.Processing;
            await context.SaveChangesAsync(stoppingToken);

            try
            {
                // Check for deduplication — skip if same Jellyfin item already has a linked MediaItem
                var existingLink = await context.JellyfinLibraryItems
                    .FirstOrDefaultAsync(j => j.JellyfinItemId == item.JellyfinItemId && j.MediaItemId != null, stoppingToken);

                if (existingLink is not null)
                {
                    _logger.LogDebug("Skipping import for {JellyfinItemId} — already linked to MediaItem {MediaItemId}",
                        item.JellyfinItemId, existingLink.MediaItemId);
                    item.Status = ImportStatus.Completed;
                    await context.SaveChangesAsync(stoppingToken);
                    return;
                }

                // Check blacklist
                var isBlacklisted = await context.BlacklistedItems
                    .AnyAsync(b => b.JellyfinItemId == item.JellyfinItemId, stoppingToken);

                if (isBlacklisted)
                {
                    _logger.LogDebug("Discarding blacklisted import queue item {JellyfinItemId}", item.JellyfinItemId);
                    item.Status = ImportStatus.Failed;
                    await context.SaveChangesAsync(stoppingToken);
                    return;
                }

                // Get item info from Jellyfin to resolve metadata
                // Use /Users/{userId}/Items/{id} — the admin-only /Items/{id} endpoint returns 400
                var jellyfinClient = scope.ServiceProvider.GetRequiredService<Jellywatch.Api.Services.Jellyfin.IJellyfinApiClient>();
                var anyUserId = await context.Profiles
                    .Select(p => p.JellyfinUserId)
                    .FirstOrDefaultAsync(stoppingToken);
                if (anyUserId is null)
                {
                    _logger.LogWarning("No profiles found to resolve Jellyfin item {JellyfinItemId}", item.JellyfinItemId);
                    item.Status = ImportStatus.Pending;
                    item.NextRetryAt = DateTime.UtcNow.AddMinutes(5);
                    await context.SaveChangesAsync(stoppingToken);
                    return;
                }
                var jellyfinItem = await jellyfinClient.GetItemAsync(item.JellyfinItemId, anyUserId);

                if (jellyfinItem is null)
                {
                    _logger.LogWarning("Jellyfin item not found: {JellyfinItemId}", item.JellyfinItemId);
                    item.Status = ImportStatus.Failed;
                    await context.SaveChangesAsync(stoppingToken);
                    return;
                }

                MediaItem? mediaItem;

                // Extract provider IDs from Jellyfin item
                int? tmdbId = null;
                string? imdbId = null;

                if (jellyfinItem.ProviderIds is not null)
                {
                    if (jellyfinItem.ProviderIds.TryGetValue("Tmdb", out var tmdbStr) && int.TryParse(tmdbStr, out var parsedTmdb))
                        tmdbId = parsedTmdb;
                    if (jellyfinItem.ProviderIds.TryGetValue("Imdb", out var imdbStr))
                        imdbId = imdbStr;
                }

                int? year = null;
                if (!string.IsNullOrWhiteSpace(jellyfinItem.PremiereDate))
                {
                    if (DateTime.TryParse(jellyfinItem.PremiereDate, out var premiere))
                        year = premiere.Year;
                }

                if (jellyfinItem.Type == "Episode" && !string.IsNullOrWhiteSpace(jellyfinItem.SeriesId))
                {
                    // Episode item — resolve/deduplicate by the parent series
                    mediaItem = await metadataService.ResolveSeriesAsync(
                        jellyfinItem.SeriesId, jellyfinItem.SeriesName ?? "Unknown",
                        year: null, tmdbId: null, imdbId: null);

                    if (mediaItem is not null)
                    {
                        var series = await context.Series.FirstOrDefaultAsync(s => s.MediaItemId == mediaItem.Id, stoppingToken);
                        if (series is not null)
                            await metadataService.PopulateSeasonsAndEpisodesAsync(series.Id);
                    }
                }
                else if (jellyfinItem.Type == "Movie")
                {
                    mediaItem = await metadataService.ResolveMovieAsync(
                        item.JellyfinItemId, jellyfinItem.Name ?? "Unknown", year, tmdbId, imdbId);
                }
                else
                {
                    // Plain Series item (or unknown type — fall back to queued MediaType)
                    mediaItem = await metadataService.ResolveSeriesAsync(
                        item.JellyfinItemId, jellyfinItem.Name ?? "Unknown", year, tmdbId, imdbId);

                    if (mediaItem is not null)
                    {
                        var series = await context.Series.FirstOrDefaultAsync(s => s.MediaItemId == mediaItem.Id, stoppingToken);
                        if (series is not null)
                            await metadataService.PopulateSeasonsAndEpisodesAsync(series.Id);
                    }
                }

                if (mediaItem is not null)
                {
                    await metadataService.RefreshTranslationsAsync(mediaItem.Id);
                    await metadataService.RefreshImagesAsync(mediaItem.Id);

                    item.Status = ImportStatus.Completed;
                    _logger.LogInformation("Successfully imported {MediaType} '{Name}' (JellyfinId: {JellyfinItemId})",
                        item.MediaType, jellyfinItem.Name, item.JellyfinItemId);
                }
                else
                {
                    throw new InvalidOperationException($"Could not resolve metadata for '{jellyfinItem.Name}'");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process import queue item {ItemId} (JellyfinId: {JellyfinItemId})",
                    itemId, item.JellyfinItemId);

                item.RetryCount++;
                if (item.RetryCount >= MaxRetries)
                {
                    item.Status = ImportStatus.Failed;
                    _logger.LogWarning("Import queue item {ItemId} failed after {MaxRetries} retries", itemId, MaxRetries);
                }
                else
                {
                    item.Status = ImportStatus.Pending;
                    item.NextRetryAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, item.RetryCount));
                }
            }

            await context.SaveChangesAsync(stoppingToken);
        }
        finally
        {
            _concurrencyLimiter.Release();
        }
    }
}
