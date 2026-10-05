using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Application.Services;
using Jellywatch.Api.Configuration;
using Microsoft.Extensions.Options;

namespace Jellywatch.Api.Infrastructure.BackgroundJobs;

public sealed class BulkMetadataWorker(IServiceScopeFactory scopes, MetadataWriteGate gate, IOptions<WebPushSettings> options,
    ILogger<BulkMetadataWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (settings.MetadataDailyHour is < 0 or > 23 || settings.MetadataItemDelaySeconds < 1)
            throw new InvalidOperationException("Metadata daily hour must be 0–23 and item delay must be at least one second.");
        while (!stoppingToken.IsCancellationRequested)
        {
            await gate.Semaphore.WaitAsync(stoppingToken);
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<BulkMetadataService>();
                var queued = await service.EnsureDailyCycleAsync(DateTimeOffset.UtcNow, settings, stoppingToken);
                if (queued is not null) logger.LogInformation("Daily metadata cycle uses job {JobId} with {Total} titles.", queued.Id, queued.Total);
                await service.ProcessNextAsync(
                    scope.ServiceProvider.GetRequiredService<IMetadataResolutionService>(), logger, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Bulk metadata worker failed; checkpoint will be retried."); }
            finally { gate.Semaphore.Release(); }
            // Delay after work completes, rather than a periodic timer whose overdue ticks
            // can cause back-to-back external requests when an item takes several seconds.
            await Task.Delay(TimeSpan.FromSeconds(settings.MetadataItemDelaySeconds), stoppingToken);
        }
    }
}
