using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure;
using Jellywatch.Api.Services.Metadata;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class DataController : BaseApiController
{
    private readonly JellywatchDbContext _context;
    private readonly ILogger<DataController> _logger;
    private readonly IMetadataResolutionService _metadata;

    private static readonly string[] CsvHeaders =
    {
        "type", "title", "tmdb_id", "imdb_id",
        "season_number", "episode_number", "episode_name",
        "state", "rating", "watched_at"
    };

    public DataController(JellywatchDbContext context, ILogger<DataController> logger, IMetadataResolutionService metadata)
    {
        _context = context;
        _logger = logger;
        _metadata = metadata;
    }

    // ━━━━ EXPORT ━━━━

    [HttpGet("{profileId:int}/export")]
    public async Task<IActionResult> Export(int profileId)
    {
        var profile = await _context.Profiles.FindAsync(profileId);
        if (profile is null)
            return NotFound(new { message = "Profile not found" });

        // Episode watch states
        var episodeStates = await _context.ProfileWatchStates
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
                WatchedAt = _context.WatchEvents
                    .Where(e => e.ProfileId == profileId
                        && e.EpisodeId == s.EpisodeId
                        && e.EventType == WatchEventType.Finished)
                    .OrderByDescending(e => e.Timestamp)
                    .Select(e => (DateTime?)e.Timestamp)
                    .FirstOrDefault(),
            })
            .ToListAsync();

        // Movie watch states
        var movieStates = await _context.ProfileWatchStates
            .Where(s => s.ProfileId == profileId && s.MovieId != null)
            .Select(s => new
            {
                MediaTitle = s.MediaItem.Title,
                TmdbId = s.MediaItem.TmdbId,
                ImdbId = s.MediaItem.ImdbId,
                s.State,
                s.UserRating,
                WatchedAt = _context.WatchEvents
                    .Where(e => e.ProfileId == profileId
                        && e.MovieId == s.MovieId
                        && e.EventType == WatchEventType.Finished)
                    .OrderByDescending(e => e.Timestamp)
                    .Select(e => (DateTime?)e.Timestamp)
                    .FirstOrDefault(),
            })
            .ToListAsync();

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

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"jellywatch-export-{profileId}-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    // ━━━━ IMPORT PREVIEW ━━━━

    [HttpPost("{profileId:int}/import/preview")]
    public async Task<ActionResult<ImportPreviewDto>> ImportPreview(int profileId, IFormFile file)
    {
        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { message = "File too large (max 10 MB)" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext != ".csv")
            return BadRequest(new { message = "Only CSV files are supported" });

        var rows = await ParseCsvAsync(file);
        if (rows.Count == 0)
            return BadRequest(new { message = "CSV is empty or has no valid rows" });

        var errors = new List<string>();
        var validRows = new List<ImportRowDto>();
        var duplicateCount = 0;
        var notFoundCount = 0;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (!ValidateRow(row, i + 2, errors))
                continue;

            // Check for existing media by tmdb_id
            var isDuplicate = false;
            var isNotFound = false;
            var willBeAdded = false;
            if (row.TmdbId.HasValue)
            {
                var existing = await _context.MediaItems.AnyAsync(m => m.TmdbId == row.TmdbId);
                if (existing) isDuplicate = true;
                else willBeAdded = true;
            }
            else
            {
                isNotFound = true;
            }

            if (isDuplicate) duplicateCount++;
            if (isNotFound) notFoundCount++;
            validRows.Add(new ImportRowDto
            {
                Type = row.Type,
                Title = row.Title,
                TmdbId = row.TmdbId,
                ImdbId = row.ImdbId,
                SeasonNumber = row.SeasonNumber,
                EpisodeNumber = row.EpisodeNumber,
                EpisodeName = row.EpisodeName,
                State = row.State,
                Rating = row.Rating,
                WatchedAt = row.WatchedAt,
                IsDuplicate = isDuplicate,
                IsNotFound = isNotFound,
                WillBeAdded = willBeAdded,
            });
        }

        return Ok(new ImportPreviewDto
        {
            TotalRows = rows.Count,
            ValidRows = validRows.Count,
            DuplicateRows = duplicateCount,
            NotFoundRows = notFoundCount,
            Errors = errors.Take(50).ToList(),
            Rows = validRows.Take(100).ToList(),
        });
    }

    // ━━━━ IMPORT EXECUTE ━━━━

    [HttpPost("{profileId:int}/import")]
    public async Task<ActionResult<ImportResultDto>> Import(
        int profileId,
        IFormFile file,
        [FromQuery] bool skipDuplicates = true,
        [FromQuery] bool overwriteDates = false)
    {
        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { message = "File too large (max 10 MB)" });

        var rows = await ParseCsvAsync(file);
        var imported = 0;
        var skipped = 0;
        var errors = new List<string>();

        foreach (var (row, index) in rows.Select((r, i) => (r, i)))
        {
            if (!ValidateRow(row, index + 2, errors)) continue;

            // Find media by TMDB ID
            if (!row.TmdbId.HasValue)
            {
                errors.Add($"Row {index + 2}: No TMDB ID — skipped");
                skipped++;
                continue;
            }

            var mediaItem = await _context.MediaItems
                .FirstOrDefaultAsync(m => m.TmdbId == row.TmdbId);

            if (mediaItem is null)
            {
                // Auto-add missing media from TMDB
                try
                {
                    var syntheticId = $"csv-import-{row.TmdbId}";
                    if (row.Type == "episode")
                    {
                        mediaItem = await _metadata.ResolveSeriesAsync(syntheticId, row.Title, tmdbId: row.TmdbId);
                        if (mediaItem != null)
                        {
                            var series = await _context.Series.FirstOrDefaultAsync(s => s.MediaItemId == mediaItem.Id);
                            if (series != null)
                                await _metadata.PopulateSeasonsAndEpisodesAsync(series.Id);
                            await _metadata.RefreshTranslationsAsync(mediaItem.Id);
                            await _metadata.RefreshImagesAsync(mediaItem.Id);
                        }
                    }
                    else if (row.Type == "movie")
                    {
                        mediaItem = await _metadata.ResolveMovieAsync(syntheticId, row.Title, tmdbId: row.TmdbId);
                        if (mediaItem != null)
                        {
                            await _metadata.RefreshTranslationsAsync(mediaItem.Id);
                            await _metadata.RefreshImagesAsync(mediaItem.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to auto-add TMDB {TmdbId} ({Title})", row.TmdbId, row.Title);
                }

                if (mediaItem is null)
                {
                    errors.Add($"Row {index + 2}: '{row.Title}' (TMDB {row.TmdbId}) could not be added from TMDB");
                    skipped++;
                    continue;
                }
            }

            if (row.Type == "episode")
            {
                // Find the episode
                var episode = await _context.Episodes
                    .Include(e => e.Season)
                    .Where(e => e.Season.Series.MediaItemId == mediaItem.Id
                        && e.Season.SeasonNumber == row.SeasonNumber
                        && e.EpisodeNumber == row.EpisodeNumber)
                    .FirstOrDefaultAsync();

                if (episode is null) { skipped++; continue; }

                // Upsert watch state
                var state = await _context.ProfileWatchStates
                    .FirstOrDefaultAsync(s => s.ProfileId == profileId && s.EpisodeId == episode.Id);

                if (state is not null && skipDuplicates) { skipped++; continue; }

                if (state is null)
                {
                    state = new ProfileWatchState
                    {
                        ProfileId = profileId,
                        MediaItemId = mediaItem.Id,
                        EpisodeId = episode.Id,
                    };
                    _context.ProfileWatchStates.Add(state);
                }

                if (Enum.TryParse<WatchState>(row.State, true, out var ws))
                    state.State = ws;
                if (row.Rating.HasValue)
                    state.UserRating = row.Rating;
                state.IsManualOverride = true;
                state.LastUpdated = DateTime.UtcNow;

                // Create watch event if we have a timestamp
                if (row.WatchedAt.HasValue && ws == WatchState.Seen)
                {
                    var existingEvent = await _context.WatchEvents.FirstOrDefaultAsync(e =>
                        e.ProfileId == profileId
                        && e.EpisodeId == episode.Id
                        && e.EventType == WatchEventType.Finished);
                    if (existingEvent is null)
                    {
                        _context.WatchEvents.Add(new WatchEvent
                        {
                            ProfileId = profileId,
                            MediaItemId = mediaItem.Id,
                            EpisodeId = episode.Id,
                            EventType = WatchEventType.Finished,
                            Source = SyncSource.Manual,
                            Timestamp = row.WatchedAt.Value,
                        });
                    }
                    else if (overwriteDates)
                    {
                        existingEvent.Timestamp = row.WatchedAt.Value;
                    }
                }

                imported++;
            }
            else if (row.Type == "movie")
            {
                var movie = await _context.Movies
                    .FirstOrDefaultAsync(m => m.MediaItemId == mediaItem.Id);

                if (movie is null) { skipped++; continue; }

                var state = await _context.ProfileWatchStates
                    .FirstOrDefaultAsync(s => s.ProfileId == profileId && s.MovieId == movie.Id);

                if (state is not null && skipDuplicates) { skipped++; continue; }

                if (state is null)
                {
                    state = new ProfileWatchState
                    {
                        ProfileId = profileId,
                        MediaItemId = mediaItem.Id,
                        MovieId = movie.Id,
                    };
                    _context.ProfileWatchStates.Add(state);
                }

                if (Enum.TryParse<WatchState>(row.State, true, out var ws))
                    state.State = ws;
                if (row.Rating.HasValue)
                    state.UserRating = row.Rating;
                state.IsManualOverride = true;
                state.LastUpdated = DateTime.UtcNow;

                if (row.WatchedAt.HasValue && ws == WatchState.Seen)
                {
                    var existingEvent = await _context.WatchEvents.FirstOrDefaultAsync(e =>
                        e.ProfileId == profileId
                        && e.MovieId == movie.Id
                        && e.EventType == WatchEventType.Finished);
                    if (existingEvent is null)
                    {
                        _context.WatchEvents.Add(new WatchEvent
                        {
                            ProfileId = profileId,
                            MediaItemId = mediaItem.Id,
                            MovieId = movie.Id,
                            EventType = WatchEventType.Finished,
                            Source = SyncSource.Manual,
                            Timestamp = row.WatchedAt.Value,
                        });
                    }
                    else if (overwriteDates)
                    {
                        existingEvent.Timestamp = row.WatchedAt.Value;
                    }
                }

                imported++;
            }
        }

        await _context.SaveChangesAsync();

        return Ok(new ImportResultDto
        {
            Imported = imported,
            Skipped = skipped,
            Errors = errors.Take(50).ToList(),
        });
    }

    // ━━━━ Helpers ━━━━

    private static string CsvEscape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }

    private static async Task<List<CsvRow>> ParseCsvAsync(IFormFile file)
    {
        var rows = new List<CsvRow>();
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);

        var header = await reader.ReadLineAsync();
        if (header is null) return rows;

        while (await reader.ReadLineAsync() is { } line)
        {
            var fields = ParseCsvLine(line);
            if (fields.Length < 10) continue;

            rows.Add(new CsvRow
            {
                Type = fields[0].Trim().ToLowerInvariant(),
                Title = fields[1].Trim(),
                TmdbId = int.TryParse(fields[2].Trim(), out var tmdb) ? tmdb : null,
                ImdbId = string.IsNullOrWhiteSpace(fields[3]) ? null : fields[3].Trim(),
                SeasonNumber = int.TryParse(fields[4].Trim(), out var sn) ? sn : null,
                EpisodeNumber = int.TryParse(fields[5].Trim(), out var en) ? en : null,
                EpisodeName = string.IsNullOrWhiteSpace(fields[6]) ? null : fields[6].Trim(),
                State = fields[7].Trim(),
                Rating = decimal.TryParse(fields[8].Trim(), CultureInfo.InvariantCulture, out var r) ? r : null,
                WatchedAt = DateTime.TryParse(fields[9].Trim(), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dt) ? dt.ToUniversalTime() : null,
            });
        }

        return rows;
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"') inQuotes = true;
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private static bool ValidateRow(CsvRow row, int lineNumber, List<string> errors)
    {
        if (row.Type != "episode" && row.Type != "movie")
        {
            errors.Add($"Row {lineNumber}: Invalid type '{row.Type}'");
            return false;
        }
        if (string.IsNullOrWhiteSpace(row.Title))
        {
            errors.Add($"Row {lineNumber}: Missing title");
            return false;
        }
        if (row.Type == "episode" && (row.SeasonNumber is null || row.EpisodeNumber is null))
        {
            errors.Add($"Row {lineNumber}: Episode missing season/episode number");
            return false;
        }
        return true;
    }

    private record CsvRow
    {
        public string Type { get; init; } = "";
        public string Title { get; init; } = "";
        public int? TmdbId { get; init; }
        public string? ImdbId { get; init; }
        public int? SeasonNumber { get; init; }
        public int? EpisodeNumber { get; init; }
        public string? EpisodeName { get; init; }
        public string State { get; init; } = "";
        public decimal? Rating { get; init; }
        public DateTime? WatchedAt { get; init; }
    }
}

// DTOs
public class ImportPreviewDto
{
    public int TotalRows { get; set; }
    public int ValidRows { get; set; }
    public int DuplicateRows { get; set; }
    public int NotFoundRows { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<ImportRowDto> Rows { get; set; } = new();
}

public class ImportRowDto
{
    public string Type { get; set; } = "";
    public string Title { get; set; } = "";
    public int? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public int? SeasonNumber { get; set; }
    public int? EpisodeNumber { get; set; }
    public string? EpisodeName { get; set; }
    public string State { get; set; } = "";
    public decimal? Rating { get; set; }
    public DateTime? WatchedAt { get; set; }
    public bool IsDuplicate { get; set; }
    public bool IsNotFound { get; set; }
    public bool WillBeAdded { get; set; }
}

public class ImportResultDto
{
    public int Imported { get; set; }
    public int Skipped { get; set; }
    public List<string> Errors { get; set; } = new();
}
