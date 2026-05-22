using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure.Persistence;

namespace Jellywatch.Api.Infrastructure.BackgroundJobs;

public class BackupScheduleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackupScheduleService> _logger;

    private static readonly string[] CsvHeaders =
    {
        "type", "title", "tmdb_id", "imdb_id",
        "season_number", "episode_number", "episode_name",
        "state", "rating", "watched_at"
    };

    public BackupScheduleService(IServiceScopeFactory scopeFactory, ILogger<BackupScheduleService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var now = DateTime.UtcNow;
            await RunDueBackupsAsync(now, stoppingToken);
        }
    }

    private async Task RunDueBackupsAsync(DateTime now, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JellywatchDbContext>();

        var due = await db.BackupSchedules
            .Where(s => s.IsEnabled && s.BackupHour == now.Hour && s.BackupMinute == now.Minute)
            .ToListAsync(ct);

        foreach (var schedule in due)
        {
            await RunBackupAsync(db, schedule, now, ct);
        }
    }

    public async Task RunBackupForUserAsync(int userId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<JellywatchDbContext>();

        var schedule = await db.BackupSchedules.FirstOrDefaultAsync(s => s.UserId == userId, ct);
        if (schedule == null)
        {
            schedule = new BackupSchedule
            {
                UserId = userId,
                IsEnabled = false,
                BackupHour = 3,
                BackupMinute = 0,
                DestinationPath = "/backups",
                FileNamePrefix = "",
                FileNameSuffix = "",
                RetentionCount = 7
            };
            db.BackupSchedules.Add(schedule);
            await db.SaveChangesAsync(ct);
        }

        await RunBackupAsync(db, schedule, DateTime.UtcNow, ct);
    }

    private async Task RunBackupAsync(JellywatchDbContext db, BackupSchedule schedule, DateTime now, CancellationToken ct)
    {
        schedule.LastRunStatus = "running";
        schedule.LastRunAt = now;
        await db.SaveChangesAsync(ct);

        try
        {
            var dest = schedule.DestinationPath.TrimEnd('/', '\\');
            Directory.CreateDirectory(dest);

            // Export one CSV file per profile belonging to this user
            var profiles = await db.Profiles
                .Where(p => p.UserId == schedule.UserId)
                .ToListAsync(ct);

            if (profiles.Count == 0)
            {
                schedule.LastRunStatus = "success";
                schedule.LastRunMessage = "No profiles found — nothing to back up.";
                await db.SaveChangesAsync(ct);
                return;
            }

            var filesWritten = 0;
            foreach (var profile in profiles)
            {
                var csvBytes = await BuildProfileCsvAsync(db, profile.Id, ct);

                var prefix = string.IsNullOrWhiteSpace(schedule.FileNamePrefix) ? "" : $"{schedule.FileNamePrefix.Trim()}-";
                var suffix = string.IsNullOrWhiteSpace(schedule.FileNameSuffix) ? "" : $"-{schedule.FileNameSuffix.Trim()}";
                var safeName = SanitizeFileName(profile.DisplayName);
                var date = now.ToString("yyyyMMdd");
                var fileName = $"{prefix}profile-{profile.Id}-{safeName}-{date}{suffix}.csv";
                var filePath = Path.Combine(dest, fileName);

                await File.WriteAllBytesAsync(filePath, csvBytes, ct);
                filesWritten++;
            }

            // ── Retention: keep only the most recent N files per profile ──
            if (schedule.RetentionCount > 0)
            {
                foreach (var profile in profiles)
                {
                    var pattern = $"*profile-{profile.Id}-*";
                    var existing = Directory.GetFiles(dest, pattern)
                        .OrderByDescending(f => f)
                        .Skip(schedule.RetentionCount)
                        .ToList();

                    foreach (var old in existing)
                        File.Delete(old);
                }
            }

            schedule.LastRunStatus = "success";
            schedule.LastRunMessage = $"Backed up {filesWritten} profile(s) to {dest}";
            _logger.LogInformation("Backup completed for user {UserId}: {Count} profile(s)", schedule.UserId, filesWritten);
        }
        catch (Exception ex)
        {
            schedule.LastRunStatus = "failed";
            schedule.LastRunMessage = ex.Message;
            _logger.LogError(ex, "Backup failed for user {UserId}", schedule.UserId);
        }
        finally
        {
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<byte[]> BuildProfileCsvAsync(JellywatchDbContext db, int profileId, CancellationToken ct)
    {
        var episodeStates = await db.ProfileWatchStates
            .Where(s => s.ProfileId == profileId && s.EpisodeId != null)
            .Select(s => new
            {
                MediaTitle = s.MediaItem.Title,
                TmdbId = s.MediaItem.TmdbId,
                ImdbId = s.MediaItem.ImdbId,
                SeasonNumber = s.Episode!.Season.SeasonNumber,
                s.Episode.EpisodeNumber,
                EpisodeName = s.Episode.Name,
                s.State,
                s.UserRating,
                WatchedAt = db.WatchEvents
                    .Where(e => e.ProfileId == profileId
                        && e.EpisodeId == s.EpisodeId
                        && e.EventType == WatchEventType.Finished)
                    .OrderByDescending(e => e.Timestamp)
                    .Select(e => (DateTime?)e.Timestamp)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var movieStates = await db.ProfileWatchStates
            .Where(s => s.ProfileId == profileId && s.MovieId != null)
            .Select(s => new
            {
                MediaTitle = s.MediaItem.Title,
                TmdbId = s.MediaItem.TmdbId,
                ImdbId = s.MediaItem.ImdbId,
                s.State,
                s.UserRating,
                WatchedAt = db.WatchEvents
                    .Where(e => e.ProfileId == profileId
                        && e.MovieId == s.MovieId
                        && e.EventType == WatchEventType.Finished)
                    .OrderByDescending(e => e.Timestamp)
                    .Select(e => (DateTime?)e.Timestamp)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", CsvHeaders));

        foreach (var e in episodeStates)
        {
            sb.AppendLine(string.Join(",",
                "episode",
                CsvEscape(e.MediaTitle),
                e.TmdbId?.ToString() ?? "",
                CsvEscape(e.ImdbId ?? ""),
                e.SeasonNumber,
                e.EpisodeNumber,
                CsvEscape(e.EpisodeName ?? ""),
                e.State.ToString(),
                e.UserRating?.ToString(CultureInfo.InvariantCulture) ?? "",
                e.WatchedAt?.ToString("o") ?? ""));
        }

        foreach (var m in movieStates)
        {
            sb.AppendLine(string.Join(",",
                "movie",
                CsvEscape(m.MediaTitle),
                m.TmdbId?.ToString() ?? "",
                CsvEscape(m.ImdbId ?? ""),
                "", "", "",
                m.State.ToString(),
                m.UserRating?.ToString(CultureInfo.InvariantCulture) ?? "",
                m.WatchedAt?.ToString("o") ?? ""));
        }

        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return clean.Replace(' ', '_').ToLowerInvariant();
    }
}
