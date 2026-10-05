using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Configuration;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jellywatch.Api.Infrastructure.BackgroundJobs;

public sealed class MetadataRefreshWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<WebPushSettings> options,
    ILogger<MetadataRefreshWorker> logger) : BackgroundService
{
    private readonly WebPushSettings _settings = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.MetadataRefreshEnabled)
        {
            logger.LogInformation("Periodic Jellywatch metadata refresh is disabled.");
            return;
        }

        // Run once shortly after startup, then scan eligible series incrementally.
        try { await RefreshBatchAsync(stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        catch (Exception exception) { logger.LogError(exception, "Initial Jellywatch metadata refresh failed."); }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Clamp(_settings.MetadataScanIntervalMinutes, 15, 10080)));
        do
        {
            try { await RefreshBatchAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Periodic Jellywatch metadata refresh failed."); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RefreshBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<JellywatchDbContext>();
        var metadata = scope.ServiceProvider.GetRequiredService<IMetadataResolutionService>();
        var candidates = await context.MediaItems.AsNoTracking()
            .Where(item => item.MediaType == MediaType.Series && item.TmdbId != null && item.Series != null)
            .Select(item => new { item.Id, item.Status })
            .ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var candidateIds = candidates.Select(candidate => candidate.Id).ToList();
        var existingJobs = (await context.MetadataRefreshJobs
                .Where(job => job.Provider == ExternalProvider.Tmdb && candidateIds.Contains(job.MediaItemId))
                .ToListAsync(cancellationToken))
            .GroupBy(job => job.MediaItemId)
            .ToDictionary(group => group.Key, group => group.OrderBy(job => job.NextRefresh).ThenBy(job => job.Id).First());
        var duplicateJobs = (await context.MetadataRefreshJobs
                .Where(job => job.Provider == ExternalProvider.Tmdb && candidateIds.Contains(job.MediaItemId))
                .ToListAsync(cancellationToken))
            .GroupBy(job => job.MediaItemId)
            .SelectMany(group => group.OrderBy(job => job.NextRefresh).ThenBy(job => job.Id).Skip(1))
            .ToList();
        if (duplicateJobs.Count > 0)
        {
            context.MetadataRefreshJobs.RemoveRange(duplicateJobs);
            await context.SaveChangesAsync(cancellationToken);
        }

        var due = candidates
            .Where(candidate => !existingJobs.TryGetValue(candidate.Id, out var job) || job.NextRefresh <= now)
            .OrderBy(candidate => IsAiring(candidate.Status) ? 0 : 1)
            .ThenBy(candidate => existingJobs.TryGetValue(candidate.Id, out var job) ? job.NextRefresh : DateTime.MinValue)
            .Take(Math.Clamp(_settings.MetadataBatchSize, 1, 100))
            .ToList();
        if (due.Count == 0) return;

        // Rate/cost-aware cadence: only actively airing or announced upcoming series are polled daily;
        // completed series are revisited weekly. Each invocation takes a small batch.
        var selected = new List<int>(due.Count);
        foreach (var candidate in due)
        {
            if (!existingJobs.TryGetValue(candidate.Id, out var job))
            {
                job = new MetadataRefreshJob { MediaItemId = candidate.Id, Provider = ExternalProvider.Tmdb, NextRefresh = now };
                context.MetadataRefreshJobs.Add(job);
                existingJobs.Add(candidate.Id, job);
            }
            selected.Add(candidate.Id);
        }
        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Refreshing metadata for batch of {Count} series.", selected.Count);
        foreach (var mediaItemId in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // Force TMDB detail and season cache refresh so locally missing seasons are found,
                // including during the initial scan as requested.
                await metadata.RefreshMediaItemAsync(mediaItemId, refreshImages: false, cancellationToken: cancellationToken);
                var job = await context.MetadataRefreshJobs.FirstAsync(
                    row => row.MediaItemId == mediaItemId && row.Provider == ExternalProvider.Tmdb, cancellationToken);
                job.LastRefreshed = now;
                var status = await context.MediaItems.AsNoTracking().Where(x => x.Id == mediaItemId).Select(x => x.Status).FirstOrDefaultAsync(cancellationToken);
                var isAiring = IsAiring(status);
                var firstEpisodeDates = await context.Episodes.AsNoTracking()
                    .Where(episode => episode.Season.Series.MediaItemId == mediaItemId
                        && episode.EpisodeNumber == 1
                        && episode.AirDate != null)
                    .Select(episode => episode.AirDate)
                    .ToListAsync(cancellationToken);
                var today = DateOnly.FromDateTime(now);
                var hasUpcomingPremiere = firstEpisodeDates.Any(date =>
                    DateOnly.TryParse(date, out var airDate) && airDate >= today);
                job.NextRefresh = now.AddDays(isAiring || hasUpcomingPremiere ? 1 : 7);
                job.Status = ImportStatus.Completed;
                await context.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Metadata refresh failed for media item {MediaItemId}.", mediaItemId);
                var job = await context.MetadataRefreshJobs.FirstOrDefaultAsync(
                    row => row.MediaItemId == mediaItemId && row.Provider == ExternalProvider.Tmdb, cancellationToken);
                if (job is not null)
                {
                    job.Status = ImportStatus.Failed;
                    job.NextRefresh = now.AddHours(6);
                    await context.SaveChangesAsync(cancellationToken);
                }
            }
            context.ChangeTracker.Clear();
        }
    }

    private static bool IsAiring(string? status) =>
        string.Equals(status, "Returning Series", StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, "In Production", StringComparison.OrdinalIgnoreCase);
}
