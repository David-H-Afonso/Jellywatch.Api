using System.Text.Json;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Configuration;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jellywatch.Api.Application.Services;

public sealed class BulkMetadataService(JellywatchDbContext context)
{
    public async Task<BulkMetadataJobDto?> EnsureDailyCycleAsync(DateTimeOffset now, WebPushSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!settings.MetadataRefreshEnabled) return null;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.MetadataTimeZoneId);
        var local = TimeZoneInfo.ConvertTime(now, zone);
        if (local.Hour < settings.MetadataDailyHour) return null;
        var date = local.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        // Persist the daily marker and the queued job atomically. Missed days are not replayed;
        // starting after 03:00 catches up only today's cycle.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        if (await context.Set<MetadataDailyCycle>().AnyAsync(cycle => cycle.LocalDate == date, cancellationToken))
            return null;
        var job = await EnqueueAsync(cancellationToken);
        context.Add(new MetadataDailyCycle { LocalDate = date, JobId = job.Id, CreatedAtUtc = now.UtcDateTime });
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return job;
    }

    public async Task<BulkMetadataJobDto> EnqueueAsync(CancellationToken cancellationToken = default)
    {
        var active = await context.Set<BulkMetadataJob>().AsNoTracking()
            .FirstOrDefaultAsync(job => job.ActiveKey == "metadata", cancellationToken);
        if (active is not null) return ToDto(active);
        var ids = await context.MediaItems.OrderBy(item => item.Id).Select(item => item.Id).ToListAsync(cancellationToken);
        var job = new BulkMetadataJob
        {
            MediaItemIdsJson = JsonSerializer.Serialize(ids), Total = ids.Count, CreatedAt = DateTime.UtcNow,
            Status = ids.Count == 0 ? "Completed" : "Pending",
            ActiveKey = ids.Count == 0 ? null : "metadata",
            CompletedAt = ids.Count == 0 ? DateTime.UtcNow : null
        };
        context.Add(job);
        try { await context.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            context.Entry(job).State = EntityState.Detached;
            var concurrent = await context.Set<BulkMetadataJob>().AsNoTracking()
                .FirstOrDefaultAsync(row => row.ActiveKey == "metadata", cancellationToken);
            if (concurrent is null) throw;
            return ToDto(concurrent);
        }
        return ToDto(job);
    }

    public async Task<BulkMetadataJobDto?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var job = await context.Set<BulkMetadataJob>().AsNoTracking().OrderByDescending(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return job is null ? null : ToDto(job);
    }

    public async Task ProcessNextAsync(IMetadataResolutionService metadata, ILogger logger, CancellationToken cancellationToken)
    {
        var job = await context.Set<BulkMetadataJob>().FirstOrDefaultAsync(row => row.ActiveKey == "metadata", cancellationToken);
        if (job is null) return;
        var ids = JsonSerializer.Deserialize<List<int>>(job.MediaItemIdsJson) ?? [];
        if (job.Processed < ids.Count)
        {
            var id = ids[job.Processed];
            job.Status = "Running";
            await context.SaveChangesAsync(cancellationToken);
            var succeeded = false;
            string? error = null;
            try
            {
                if (!await context.MediaItems.AnyAsync(item => item.Id == id, cancellationToken))
                    throw new KeyNotFoundException("Media item was removed after the refresh was queued.");
                await metadata.RefreshMediaItemAsync(id, refreshImages: false, cancellationToken: cancellationToken);
                succeeded = true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Bulk metadata refresh failed for media {MediaItemId}", id);
                error = $"Media {id}: {exception.GetBaseException().Message}";
                if (error.Length > 500) error = error[..500];
            }
            // Failed external operations can leave pending EF changes. Discard those before
            // persisting the checkpoint; a restart retries only the current unfinished item.
            context.ChangeTracker.Clear();
            job = await context.Set<BulkMetadataJob>().SingleAsync(row => row.Id == job.Id, cancellationToken);
            job.Processed++;
            if (succeeded) job.Succeeded++;
            else { job.Failed++; job.LastError = error; }
        }
        if (job.Processed >= ids.Count)
        {
            job.Status = job.Failed == 0 ? "Completed" : "CompletedWithErrors";
            job.ActiveKey = null;
            job.CompletedAt = DateTime.UtcNow;
        }
        await context.SaveChangesAsync(cancellationToken);
    }

    private static BulkMetadataJobDto ToDto(BulkMetadataJob job) =>
        new(job.Id, job.Status, job.Total, job.Processed, job.Succeeded, job.Failed, job.LastError);
}
