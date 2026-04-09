using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure;
using Jellywatch.Api.Services.Assets;

namespace Jellywatch.Api.Services.Metadata;

public partial class MetadataResolutionService : IMetadataResolutionService
{
    private readonly JellywatchDbContext _context;
    private readonly ITmdbApiClient _tmdbClient;
    private readonly IOmdbApiClient _omdbClient;
    private readonly ITvMazeApiClient _tvMazeClient;
    private readonly IAssetCacheService _assetService;
    private readonly ILogger<MetadataResolutionService> _logger;

    public MetadataResolutionService(
        JellywatchDbContext context,
        ITmdbApiClient tmdbClient,
        IOmdbApiClient omdbClient,
        ITvMazeApiClient tvMazeClient,
        IAssetCacheService assetService,
        ILogger<MetadataResolutionService> logger)
    {
        _context = context;
        _tmdbClient = tmdbClient;
        _omdbClient = omdbClient;
        _tvMazeClient = tvMazeClient;
        _assetService = assetService;
        _logger = logger;
    }

    public async Task<MediaItem?> ResolveSeriesAsync(string jellyfinItemId, string name, int? year = null, int? tmdbId = null, string? imdbId = null)
    {
        // Check if Jellyfin item is already linked
        var existingLink = await _context.JellyfinLibraryItems
            .Include(j => j.MediaItem)
            .FirstOrDefaultAsync(j => j.JellyfinItemId == jellyfinItemId);

        if (existingLink?.MediaItem is not null)
            return existingLink.MediaItem;

        // Strip trailing year like "Countdown (2025)" → cleanName="Countdown", effectiveYear=2025
        var cleanName = name;
        var effectiveYear = year;
        var yearMatch = YearSuffixRegex().Match(name);
        if (yearMatch.Success)
        {
            cleanName = name[..yearMatch.Index].Trim();
            if (effectiveYear is null && int.TryParse(yearMatch.Groups[1].Value, out var parsedYear))
                effectiveYear = parsedYear;
        }

        MediaItem? mediaItem = null;

        // Try TMDB first
        if (_tmdbClient.IsConfigured)
        {
            mediaItem = tmdbId.HasValue
                ? await ResolveSeriesFromTmdbByIdAsync(tmdbId.Value)
                : await ResolveSeriesFromTmdbBySearchAsync(cleanName, effectiveYear);
        }

        // Fallback to TVmaze
        if (mediaItem is null)
        {
            mediaItem = await ResolveSeriesFromTvMazeAsync(cleanName);
        }

        if (mediaItem is null)
        {
            _logger.LogWarning("Could not resolve series metadata for: {Name} (JellyfinId: {JellyfinItemId})", name, jellyfinItemId);
            return null;
        }

        // Link Jellyfin item
        await LinkJellyfinItemAsync(jellyfinItemId, name, MediaType.Series, mediaItem.Id);

        // Fetch ratings if IMDb ID available
        var effectiveImdbId = imdbId ?? mediaItem.ImdbId;
        if (!string.IsNullOrWhiteSpace(effectiveImdbId))
            await RefreshRatingsForImdbAsync(mediaItem.Id, effectiveImdbId);

        return mediaItem;
    }

    public async Task<MediaItem?> ResolveMovieAsync(string jellyfinItemId, string name, int? year = null, int? tmdbId = null, string? imdbId = null)
    {
        var existingLink = await _context.JellyfinLibraryItems
            .Include(j => j.MediaItem)
            .FirstOrDefaultAsync(j => j.JellyfinItemId == jellyfinItemId);

        if (existingLink?.MediaItem is not null)
            return existingLink.MediaItem;

        MediaItem? mediaItem = null;

        if (_tmdbClient.IsConfigured)
        {
            mediaItem = tmdbId.HasValue
                ? await ResolveMovieFromTmdbByIdAsync(tmdbId.Value)
                : await ResolveMovieFromTmdbBySearchAsync(name, year);
        }

        if (mediaItem is null)
        {
            _logger.LogWarning("Could not resolve movie metadata for: {Name} (JellyfinId: {JellyfinItemId})", name, jellyfinItemId);
            return null;
        }

        await LinkJellyfinItemAsync(jellyfinItemId, name, MediaType.Movie, mediaItem.Id);

        var effectiveImdbId = imdbId ?? mediaItem.ImdbId;
        if (!string.IsNullOrWhiteSpace(effectiveImdbId))
            await RefreshRatingsForImdbAsync(mediaItem.Id, effectiveImdbId);

        return mediaItem;
    }

    public async Task PopulateSeasonsAndEpisodesAsync(int seriesId)
    {
        var series = await _context.Series
            .Include(s => s.MediaItem)
            .Include(s => s.Seasons)
                .ThenInclude(s => s.Episodes)
            .FirstOrDefaultAsync(s => s.Id == seriesId);

        if (series?.MediaItem.TmdbId is null)
        {
            // Try TVmaze fallback
            if (series?.MediaItem.TvMazeId.HasValue == true)
            {
                await PopulateSeasonsFromTvMazeAsync(series);
            }
            return;
        }

        var tvDetails = await _tmdbClient.GetTvDetailsAsync(series.MediaItem.TmdbId.Value);
        if (tvDetails?.Seasons is null) return;

        foreach (var tmdbSeason in tvDetails.Seasons)
        {
            var existingSeason = series.Seasons.FirstOrDefault(s => s.SeasonNumber == tmdbSeason.SeasonNumber);

            if (existingSeason is null)
            {
                existingSeason = new Season
                {
                    SeriesId = seriesId,
                    SeasonNumber = tmdbSeason.SeasonNumber,
                    Name = tmdbSeason.Name,
                    Overview = tmdbSeason.Overview,
                    PosterPath = tmdbSeason.PosterPath,
                    TmdbId = tmdbSeason.Id,
                    EpisodeCount = tmdbSeason.EpisodeCount,
                    AirDate = tmdbSeason.AirDate,
                    TmdbRating = tmdbSeason.VoteAverage > 0 ? tmdbSeason.VoteAverage : null
                };
                _context.Seasons.Add(existingSeason);
                await _context.SaveChangesAsync();
            }
            else
            {
                existingSeason.Name = tmdbSeason.Name;
                existingSeason.Overview = tmdbSeason.Overview;
                existingSeason.PosterPath = tmdbSeason.PosterPath;
                existingSeason.TmdbId = tmdbSeason.Id;
                existingSeason.EpisodeCount = tmdbSeason.EpisodeCount;
                existingSeason.AirDate = tmdbSeason.AirDate;
                existingSeason.TmdbRating = tmdbSeason.VoteAverage > 0 ? tmdbSeason.VoteAverage : null;
                await _context.SaveChangesAsync();
            }

            // Fetch full season details with episodes
            var seasonDetails = await _tmdbClient.GetTvSeasonAsync(series.MediaItem.TmdbId.Value, tmdbSeason.SeasonNumber);

            // Update season rating from full season details (more accurate than summary)
            if (seasonDetails is not null && seasonDetails.VoteAverage > 0)
            {
                existingSeason.TmdbRating = seasonDetails.VoteAverage;
                await _context.SaveChangesAsync();
            }

            if (seasonDetails?.Episodes is null) continue;

            foreach (var tmdbEp in seasonDetails.Episodes)
            {
                var existingEp = existingSeason.Episodes.FirstOrDefault(e => e.EpisodeNumber == tmdbEp.EpisodeNumber);

                if (existingEp is null)
                {
                    _context.Episodes.Add(new Episode
                    {
                        SeasonId = existingSeason.Id,
                        EpisodeNumber = tmdbEp.EpisodeNumber,
                        Name = tmdbEp.Name,
                        Overview = tmdbEp.Overview,
                        StillPath = tmdbEp.StillPath,
                        TmdbId = tmdbEp.Id,
                        AirDate = tmdbEp.AirDate,
                        Runtime = tmdbEp.Runtime,
                        TmdbRating = tmdbEp.VoteAverage > 0 ? tmdbEp.VoteAverage : null
                    });
                }
                else
                {
                    existingEp.Name = tmdbEp.Name;
                    existingEp.Overview = tmdbEp.Overview;
                    existingEp.StillPath = tmdbEp.StillPath;
                    existingEp.TmdbId = tmdbEp.Id;
                    existingEp.AirDate = tmdbEp.AirDate;
                    existingEp.Runtime = tmdbEp.Runtime;
                    existingEp.TmdbRating = tmdbEp.VoteAverage > 0 ? tmdbEp.VoteAverage : null;
                }
            }

            await _context.SaveChangesAsync();
        }

        // Update series totals
        series.TotalSeasons = tvDetails.NumberOfSeasons;
        series.TotalEpisodes = tvDetails.NumberOfEpisodes;
        await _context.SaveChangesAsync();

        // Enrich episodes with air times from TVMaze (TMDB only provides date, not time)
        await EnrichEpisodesWithAirTimesAsync(series);

        _logger.LogInformation("Populated seasons/episodes for series {SeriesId} ({Name}): {Seasons} seasons, {Episodes} episodes",
            seriesId, series.MediaItem.Title, tvDetails.NumberOfSeasons, tvDetails.NumberOfEpisodes);
    }

    public async Task RefreshRatingsAsync(int mediaItemId)
    {
        var mediaItem = await _context.MediaItems.FindAsync(mediaItemId);
        if (mediaItem is null) return;

        // TMDB rating
        if (mediaItem.TmdbId.HasValue && _tmdbClient.IsConfigured)
        {
            if (mediaItem.MediaType == MediaType.Series)
            {
                var details = await _tmdbClient.GetTvDetailsAsync(mediaItem.TmdbId.Value);
                if (details is not null)
                    await UpsertRatingAsync(mediaItemId, ExternalProvider.Tmdb, details.VoteAverage.ToString("F1"), details.VoteCount);
            }
            else
            {
                var details = await _tmdbClient.GetMovieDetailsAsync(mediaItem.TmdbId.Value);
                if (details is not null)
                    await UpsertRatingAsync(mediaItemId, ExternalProvider.Tmdb, details.VoteAverage.ToString("F1"), details.VoteCount);
            }
        }

        // OMDb ratings (IMDb + RT)
        if (!string.IsNullOrWhiteSpace(mediaItem.ImdbId))
            await RefreshRatingsForImdbAsync(mediaItemId, mediaItem.ImdbId);
    }

    public async Task RefreshTranslationsAsync(int mediaItemId)
    {
        var mediaItem = await _context.MediaItems.FindAsync(mediaItemId);
        if (mediaItem?.TmdbId is null || !_tmdbClient.IsConfigured) return;

        var mediaType = mediaItem.MediaType == MediaType.Series ? "tv" : "movie";
        var translations = await _tmdbClient.GetTranslationsAsync(mediaItem.TmdbId.Value, mediaType);
        if (translations?.Translations is null) return;

        foreach (var translation in translations.Translations)
        {
            var langCode = $"{translation.Iso6391}-{translation.Iso31661}";
            var title = mediaItem.MediaType == MediaType.Series
                ? translation.Data?.Name
                : translation.Data?.Title;

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(translation.Data?.Overview))
                continue;

            var existing = await _context.MediaTranslations
                .FirstOrDefaultAsync(t => t.MediaItemId == mediaItemId && t.Language == langCode);

            if (existing is null)
            {
                _context.MediaTranslations.Add(new MediaTranslation
                {
                    MediaItemId = mediaItemId,
                    Language = langCode,
                    Title = title,
                    Overview = translation.Data?.Overview
                });
            }
            else
            {
                existing.Title = title;
                existing.Overview = translation.Data?.Overview;
            }
        }

        await _context.SaveChangesAsync();
    }

    public async Task RefreshImagesAsync(int mediaItemId)
    {
        var mediaItem = await _context.MediaItems.FindAsync(mediaItemId);
        if (mediaItem?.TmdbId is null || !_tmdbClient.IsConfigured) return;

        var mediaType = mediaItem.MediaType == MediaType.Series ? "tv" : "movie";
        var images = await _tmdbClient.GetImagesAsync(mediaItem.TmdbId.Value, mediaType, forceRefresh: true);
        if (images is null) return;

        await UpsertImagesAsync(mediaItemId, null, null, ImageType.Poster, images.Posters);
        await UpsertImagesAsync(mediaItemId, null, null, ImageType.Backdrop, images.Backdrops);
        await UpsertImagesAsync(mediaItemId, null, null, ImageType.Logo, images.Logos);

        await _context.SaveChangesAsync();
    }

    public async Task<List<PosterOptionDto>> GetPosterOptionsAsync(int mediaItemId)
    {
        var posters = await _context.MediaImages
            .Where(i => i.MediaItemId == mediaItemId
                && i.ImageType == ImageType.Poster
                && i.SeasonId == null && i.EpisodeId == null
                && i.RemoteUrl != null)
            .OrderBy(i => i.Language == "en" ? 0 : i.Language == "es" ? 1 : 2)
            .ThenBy(i => i.Id)
            .ToListAsync();

        return posters.Select(p => new PosterOptionDto
        {
            Id = p.Id,
            RemoteUrl = p.RemoteUrl!,
            ThumbnailUrl = p.RemoteUrl!.Replace("/original/", "/w185/"),
            Width = p.Width,
            Height = p.Height,
            Language = p.Language
        }).ToList();
    }

    public async Task SelectPosterAsync(int mediaItemId, string remoteUrl)
    {
        await _assetService.SelectImageAsync(mediaItemId, "poster", remoteUrl);
    }

    public async Task<List<PosterOptionDto>> GetLogoOptionsAsync(int mediaItemId)
    {
        var logos = await _context.MediaImages
            .Where(i => i.MediaItemId == mediaItemId
                && i.ImageType == ImageType.Logo
                && i.SeasonId == null && i.EpisodeId == null
                && i.RemoteUrl != null)
            .OrderBy(i => i.Id)
            .ToListAsync();

        return logos.Select(p => new PosterOptionDto
        {
            Id = p.Id,
            RemoteUrl = p.RemoteUrl!,
            ThumbnailUrl = p.RemoteUrl!.Replace("/original/", "/w300/"),
            Width = p.Width,
            Height = p.Height,
            Language = p.Language
        }).ToList();
    }

    public async Task SelectLogoAsync(int mediaItemId, string remoteUrl)
    {
        await _assetService.SelectImageAsync(mediaItemId, "logo", remoteUrl);
    }

    public async Task<int> RefreshAllMetadataAsync()
    {
        var mediaItems = await _context.MediaItems.ToListAsync();
        var count = 0;
        foreach (var item in mediaItems)
        {
            try
            {
                await RefreshMediaItemAsync(item.Id, refreshImages: false);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh metadata for {MediaItemId} '{Title}'", item.Id, item.Title);
            }
        }
        return count;
    }

    public async Task<int> RefreshAllImagesAsync()
    {
        var mediaItems = await _context.MediaItems.ToListAsync();
        var count = 0;
        foreach (var item in mediaItems)
        {
            try
            {
                await RefreshImagesAsync(item.Id);
                await _assetService.RefreshAsync(item.Id);
                count++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh images for {MediaItemId} '{Title}'", item.Id, item.Title);
            }
        }
        return count;
    }

    public async Task RefreshMediaItemAsync(int mediaItemId, int? forceTmdbId = null, bool refreshImages = true)
    {
        var mediaItem = await _context.MediaItems
            .Include(m => m.Series)
            .FirstOrDefaultAsync(m => m.Id == mediaItemId);

        if (mediaItem is null) return;

        var effectiveTmdbId = forceTmdbId ?? mediaItem.TmdbId;

        if (effectiveTmdbId.HasValue && _tmdbClient.IsConfigured)
        {
            if (mediaItem.MediaType == MediaType.Series)
            {
                var details = await _tmdbClient.GetTvDetailsAsync(effectiveTmdbId.Value);
                if (details is not null)
                {
                    mediaItem.TmdbId = details.Id;
                    mediaItem.Title = details.Name ?? mediaItem.Title;
                    mediaItem.OriginalTitle = details.OriginalName ?? mediaItem.OriginalTitle;
                    mediaItem.Overview = details.Overview ?? mediaItem.Overview;
                    mediaItem.PosterPath = details.PosterPath ?? mediaItem.PosterPath;
                    mediaItem.BackdropPath = details.BackdropPath ?? mediaItem.BackdropPath;
                    mediaItem.ReleaseDate = details.FirstAirDate ?? mediaItem.ReleaseDate;
                    mediaItem.Status = details.Status ?? mediaItem.Status;
                    mediaItem.OriginalLanguage = details.OriginalLanguage ?? mediaItem.OriginalLanguage;
                    if (details.Genres is { Count: > 0 })
                        mediaItem.Genres = string.Join(",", details.Genres.Where(g => !string.IsNullOrEmpty(g.Name)).Select(g => g.Name));
                    if (mediaItem.ImdbId is null && details.ExternalIds?.ImdbId is not null)
                        mediaItem.ImdbId = details.ExternalIds.ImdbId;
                    if (mediaItem.TvdbId is null && details.ExternalIds?.TvdbId is not null)
                        mediaItem.TvdbId = details.ExternalIds.TvdbId;

                    await _context.SaveChangesAsync();

                    if (mediaItem.Series is not null)
                    {
                        mediaItem.Series.TotalSeasons = details.NumberOfSeasons;
                        mediaItem.Series.TotalEpisodes = details.NumberOfEpisodes;
                        mediaItem.Series.Network ??= details.Networks?.FirstOrDefault()?.Name;
                        await _context.SaveChangesAsync();
                        await PopulateSeasonsAndEpisodesAsync(mediaItem.Series.Id);
                    }
                }
            }
            else
            {
                var details = await _tmdbClient.GetMovieDetailsAsync(effectiveTmdbId.Value);
                if (details is not null)
                {
                    mediaItem.TmdbId = details.Id;
                    mediaItem.Title = details.Title ?? mediaItem.Title;
                    mediaItem.OriginalTitle = details.OriginalTitle ?? mediaItem.OriginalTitle;
                    mediaItem.Overview = details.Overview ?? mediaItem.Overview;
                    mediaItem.PosterPath = details.PosterPath ?? mediaItem.PosterPath;
                    mediaItem.BackdropPath = details.BackdropPath ?? mediaItem.BackdropPath;
                    mediaItem.ReleaseDate = details.ReleaseDate ?? mediaItem.ReleaseDate;
                    mediaItem.Status = details.Status ?? mediaItem.Status;
                    mediaItem.OriginalLanguage = details.OriginalLanguage ?? mediaItem.OriginalLanguage;
                    if (details.Genres is { Count: > 0 })
                        mediaItem.Genres = string.Join(",", details.Genres.Where(g => !string.IsNullOrEmpty(g.Name)).Select(g => g.Name));
                    var effectiveImdb = details.ImdbId ?? details.ExternalIds?.ImdbId;
                    if (effectiveImdb is not null) mediaItem.ImdbId = effectiveImdb;
                    await _context.SaveChangesAsync();
                }
            }
        }

        await RefreshTranslationsAsync(mediaItemId);

        if (refreshImages)
        {
            await RefreshImagesAsync(mediaItemId);
            await _assetService.RefreshAsync(mediaItemId);
        }

        if (!string.IsNullOrWhiteSpace(mediaItem.ImdbId))
            await RefreshRatingsForImdbAsync(mediaItemId, mediaItem.ImdbId);

        _logger.LogInformation("Refreshed media item {MediaItemId} '{Title}'", mediaItemId, mediaItem.Title);
    }

    // --- Private helpers ---

    private async Task<MediaItem?> ResolveSeriesFromTmdbByIdAsync(int tmdbId)
    {
        // Check if we already have this TMDB ID
        var existing = await _context.MediaItems
            .Include(m => m.Series)
            .FirstOrDefaultAsync(m => m.TmdbId == tmdbId && m.MediaType == MediaType.Series);

        if (existing is not null) return existing;

        var details = await _tmdbClient.GetTvDetailsAsync(tmdbId);
        if (details is null) return null;

        return await CreateSeriesFromTmdbAsync(details);
    }

    private async Task<MediaItem?> ResolveSeriesFromTmdbBySearchAsync(string name, int? year)
    {
        var results = await _tmdbClient.SearchTvAsync(name, year);
        if (results.Count == 0) return null;

        var best = results[0];

        // Check if already exists
        var existing = await _context.MediaItems
            .Include(m => m.Series)
            .FirstOrDefaultAsync(m => m.TmdbId == best.Id && m.MediaType == MediaType.Series);

        if (existing is not null) return existing;

        // Get full details
        var details = await _tmdbClient.GetTvDetailsAsync(best.Id);
        if (details is null) return null;

        return await CreateSeriesFromTmdbAsync(details);
    }

    private async Task<MediaItem> CreateSeriesFromTmdbAsync(TmdbTvDetails details)
    {
        var mediaItem = new MediaItem
        {
            MediaType = MediaType.Series,
            Title = details.Name ?? "Unknown",
            OriginalTitle = details.OriginalName,
            Overview = details.Overview,
            TmdbId = details.Id,
            ImdbId = details.ExternalIds?.ImdbId,
            TvdbId = details.ExternalIds?.TvdbId,
            PosterPath = details.PosterPath,
            BackdropPath = details.BackdropPath,
            ReleaseDate = details.FirstAirDate,
            Status = details.Status,
            OriginalLanguage = details.OriginalLanguage,
            Genres = details.Genres is { Count: > 0 }
                ? string.Join(",", details.Genres.Where(g => !string.IsNullOrEmpty(g.Name)).Select(g => g.Name))
                : null
        };

        _context.MediaItems.Add(mediaItem);
        await _context.SaveChangesAsync();

        var network = details.Networks?.FirstOrDefault()?.Name;

        var series = new Series
        {
            MediaItemId = mediaItem.Id,
            TotalSeasons = details.NumberOfSeasons,
            TotalEpisodes = details.NumberOfEpisodes,
            Network = network
        };

        _context.Series.Add(series);

        // TMDB rating
        if (details.VoteCount > 0)
        {
            _context.ExternalRatings.Add(new ExternalRating
            {
                MediaItemId = mediaItem.Id,
                Provider = ExternalProvider.Tmdb,
                Score = details.VoteAverage.ToString("F1"),
                VoteCount = details.VoteCount,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Created series from TMDB: {Name} (TMDB: {TmdbId})", details.Name, details.Id);
        return mediaItem;
    }

    private async Task<MediaItem?> ResolveSeriesFromTvMazeAsync(string name)
    {
        var results = await _tvMazeClient.SearchShowsAsync(name);
        if (results.Count == 0 || results[0].Show is null) return null;

        var show = results[0].Show!;

        // Check if already exists
        var existing = await _context.MediaItems
            .FirstOrDefaultAsync(m => m.TvMazeId == show.Id && m.MediaType == MediaType.Series);

        if (existing is not null) return existing;

        var mediaItem = new MediaItem
        {
            MediaType = MediaType.Series,
            Title = show.Name ?? "Unknown",
            Overview = StripHtmlTags(show.Summary),
            TvMazeId = show.Id,
            ImdbId = show.Externals?.Imdb,
            PosterPath = show.Image?.Original,
            ReleaseDate = show.Premiered,
            Status = show.Status,
            OriginalLanguage = show.Language
        };

        _context.MediaItems.Add(mediaItem);
        await _context.SaveChangesAsync();

        var series = new Series
        {
            MediaItemId = mediaItem.Id,
            Network = show.Network?.Name,
        };

        _context.Series.Add(series);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Created series from TVmaze: {Name} (TVmaze: {TvMazeId})", show.Name, show.Id);
        return mediaItem;
    }

    private async Task EnrichEpisodesWithAirTimesAsync(Series series)
    {
        // Resolve TvMazeId from IMDB ID if not set
        if (!series.MediaItem.TvMazeId.HasValue && !string.IsNullOrWhiteSpace(series.MediaItem.ImdbId))
        {
            try
            {
                var tvMazeShow = await _tvMazeClient.LookupByImdbAsync(series.MediaItem.ImdbId);
                if (tvMazeShow is not null)
                {
                    series.MediaItem.TvMazeId = tvMazeShow.Id;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Resolved TvMazeId {TvMazeId} for series '{Title}' via IMDB {ImdbId}",
                        tvMazeShow.Id, series.MediaItem.Title, series.MediaItem.ImdbId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not resolve TvMazeId from IMDB for series '{Title}'", series.MediaItem.Title);
            }
        }

        // Fallback: resolve TvMazeId from TVDB ID if IMDB lookup failed
        if (!series.MediaItem.TvMazeId.HasValue && series.MediaItem.TvdbId.HasValue)
        {
            try
            {
                var tvMazeShow = await _tvMazeClient.LookupByTvdbAsync(series.MediaItem.TvdbId.Value);
                if (tvMazeShow is not null)
                {
                    series.MediaItem.TvMazeId = tvMazeShow.Id;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Resolved TvMazeId {TvMazeId} for series '{Title}' via TVDB {TvdbId}",
                        tvMazeShow.Id, series.MediaItem.Title, series.MediaItem.TvdbId.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not resolve TvMazeId from TVDB for series '{Title}'", series.MediaItem.Title);
            }
        }

        if (!series.MediaItem.TvMazeId.HasValue) return;

        try
        {
            // Fetch episodes and show info in parallel
            var episodesTask = _tvMazeClient.GetEpisodesAsync(series.MediaItem.TvMazeId.Value);
            var showTask = _tvMazeClient.GetShowAsync(series.MediaItem.TvMazeId.Value);
            await Task.WhenAll(episodesTask, showTask);
            var tvMazeEpisodes = episodesTask.Result;
            var tvMazeShow = showTask.Result;

            if (tvMazeEpisodes.Count == 0) return;

            // Extract schedule time + timezone so we can synthesize UTC for episodes that lack airstamp
            var scheduleTime = tvMazeShow?.Schedule?.Time; // e.g. "22:00"
            var timezone = tvMazeShow?.Network?.Country?.Timezone
                        ?? tvMazeShow?.WebChannel?.Country?.Timezone; // e.g. "Europe/Madrid"
            TimeZoneInfo? tzInfo = null;
            if (timezone is not null)
            {
                try { tzInfo = TimeZoneInfo.FindSystemTimeZoneById(timezone); }
                catch { /* unknown timezone — skip synthesis */ }
            }

            // Build lookup: include all episodes that have at least an airdate
            var lookup = tvMazeEpisodes
                .Where(e => e.Number.HasValue && (!string.IsNullOrEmpty(e.Airdate) || !string.IsNullOrEmpty(e.Airstamp)))
                .ToDictionary(e => (e.Season, e.Number!.Value), e => e);

            var updated = false;
            foreach (var season in series.Seasons)
            {
                foreach (var episode in season.Episodes)
                {
                    if (!lookup.TryGetValue((season.SeasonNumber, episode.EpisodeNumber), out var tvData))
                        continue;

                    // Update local airtime if present
                    if (!string.IsNullOrEmpty(tvData.Airtime) && episode.AirTime != tvData.Airtime)
                    {
                        episode.AirTime = tvData.Airtime;
                        updated = true;
                    }

                    // Prefer explicit airstamp; synthesize from schedule+timezone if missing
                    string? utcStamp = tvData.Airstamp;
                    if (string.IsNullOrEmpty(utcStamp) && !string.IsNullOrEmpty(tvData.Airdate) && tzInfo is not null && !string.IsNullOrEmpty(scheduleTime))
                    {
                        try
                        {
                            var localDt = DateTime.ParseExact($"{tvData.Airdate}T{scheduleTime}", "yyyy-MM-ddTHH:mm",
                                System.Globalization.CultureInfo.InvariantCulture);
                            var utcDt = TimeZoneInfo.ConvertTimeToUtc(localDt, tzInfo);
                            utcStamp = utcDt.ToString("yyyy-MM-ddTHH:mm:sszzz");
                        }
                        catch { /* malformed date — skip */ }
                    }

                    if (!string.IsNullOrEmpty(utcStamp) && episode.AirTimeUtc != utcStamp)
                    {
                        episode.AirTimeUtc = utcStamp;
                        updated = true;
                    }
                }
            }

            if (updated)
                await _context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not enrich air times from TVMaze for series {SeriesId}", series.Id);
        }
    }

    private async Task PopulateSeasonsFromTvMazeAsync(Series series)
    {
        if (!series.MediaItem.TvMazeId.HasValue) return;

        var episodes = await _tvMazeClient.GetEpisodesAsync(series.MediaItem.TvMazeId.Value);
        if (episodes.Count == 0) return;

        var grouped = episodes.GroupBy(e => e.Season).OrderBy(g => g.Key);

        foreach (var seasonGroup in grouped)
        {
            var seasonNum = seasonGroup.Key;
            var existingSeason = series.Seasons.FirstOrDefault(s => s.SeasonNumber == seasonNum);

            if (existingSeason is null)
            {
                existingSeason = new Season
                {
                    SeriesId = series.Id,
                    SeasonNumber = seasonNum,
                    Name = $"Season {seasonNum}",
                    EpisodeCount = seasonGroup.Count()
                };
                _context.Seasons.Add(existingSeason);
                await _context.SaveChangesAsync();
            }

            foreach (var tvMazeEp in seasonGroup)
            {
                if (!tvMazeEp.Number.HasValue) continue;

                var existingEp = existingSeason.Episodes.FirstOrDefault(e => e.EpisodeNumber == tvMazeEp.Number.Value);

                if (existingEp is null)
                {
                    _context.Episodes.Add(new Episode
                    {
                        SeasonId = existingSeason.Id,
                        EpisodeNumber = tvMazeEp.Number.Value,
                        Name = tvMazeEp.Name,
                        Overview = StripHtmlTags(tvMazeEp.Summary),
                        StillPath = tvMazeEp.Image?.Original,
                        AirDate = tvMazeEp.Airdate,
                        AirTime = tvMazeEp.Airtime,
                        Runtime = tvMazeEp.Runtime
                    });
                }
                else
                {
                    existingEp.Name = tvMazeEp.Name;
                    existingEp.Overview = StripHtmlTags(tvMazeEp.Summary);
                    existingEp.StillPath = tvMazeEp.Image?.Original;
                    existingEp.AirDate = tvMazeEp.Airdate;
                    existingEp.AirTime = tvMazeEp.Airtime;
                    existingEp.Runtime = tvMazeEp.Runtime;
                }
            }

            await _context.SaveChangesAsync();
        }

        series.TotalSeasons = grouped.Count();
        series.TotalEpisodes = episodes.Count(e => e.Number.HasValue);
        await _context.SaveChangesAsync();
    }

    private async Task<MediaItem?> ResolveMovieFromTmdbByIdAsync(int tmdbId)
    {
        var existing = await _context.MediaItems
            .Include(m => m.Movie)
            .FirstOrDefaultAsync(m => m.TmdbId == tmdbId && m.MediaType == MediaType.Movie);

        if (existing is not null) return existing;

        var details = await _tmdbClient.GetMovieDetailsAsync(tmdbId);
        if (details is null) return null;

        return await CreateMovieFromTmdbAsync(details);
    }

    private async Task<MediaItem?> ResolveMovieFromTmdbBySearchAsync(string name, int? year)
    {
        var results = await _tmdbClient.SearchMovieAsync(name, year);
        if (results.Count == 0) return null;

        var best = results[0];

        var existing = await _context.MediaItems
            .Include(m => m.Movie)
            .FirstOrDefaultAsync(m => m.TmdbId == best.Id && m.MediaType == MediaType.Movie);

        if (existing is not null) return existing;

        var details = await _tmdbClient.GetMovieDetailsAsync(best.Id);
        if (details is null) return null;

        return await CreateMovieFromTmdbAsync(details);
    }

    private async Task<MediaItem> CreateMovieFromTmdbAsync(TmdbMovieDetails details)
    {
        var mediaItem = new MediaItem
        {
            MediaType = MediaType.Movie,
            Title = details.Title ?? "Unknown",
            OriginalTitle = details.OriginalTitle,
            Overview = details.Overview,
            TmdbId = details.Id,
            ImdbId = details.ImdbId ?? details.ExternalIds?.ImdbId,
            PosterPath = details.PosterPath,
            BackdropPath = details.BackdropPath,
            ReleaseDate = details.ReleaseDate,
            Status = details.Status,
            OriginalLanguage = details.OriginalLanguage,
            Genres = details.Genres is { Count: > 0 }
                ? string.Join(",", details.Genres.Where(g => !string.IsNullOrEmpty(g.Name)).Select(g => g.Name))
                : null
        };

        _context.MediaItems.Add(mediaItem);
        await _context.SaveChangesAsync();

        var movie = new Movie
        {
            MediaItemId = mediaItem.Id,
            Runtime = details.Runtime
        };

        _context.Movies.Add(movie);

        if (details.VoteCount > 0)
        {
            _context.ExternalRatings.Add(new ExternalRating
            {
                MediaItemId = mediaItem.Id,
                Provider = ExternalProvider.Tmdb,
                Score = details.VoteAverage.ToString("F1"),
                VoteCount = details.VoteCount,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Created movie from TMDB: {Name} (TMDB: {TmdbId})", details.Title, details.Id);
        return mediaItem;
    }

    private async Task RefreshRatingsForImdbAsync(int mediaItemId, string imdbId)
    {
        if (!_omdbClient.IsConfigured) return;

        var omdbResult = await _omdbClient.GetByImdbIdAsync(imdbId);
        if (omdbResult is null) return;

        // IMDb rating
        if (!string.IsNullOrWhiteSpace(omdbResult.ImdbRating) && omdbResult.ImdbRating != "N/A")
        {
            int? votes = null;
            if (!string.IsNullOrWhiteSpace(omdbResult.ImdbVotes) && omdbResult.ImdbVotes != "N/A")
            {
                var cleanVotes = omdbResult.ImdbVotes.Replace(",", "");
                if (int.TryParse(cleanVotes, out var parsedVotes))
                    votes = parsedVotes;
            }

            await UpsertRatingAsync(mediaItemId, ExternalProvider.Imdb, omdbResult.ImdbRating, votes);
        }

        // Rotten Tomatoes from OMDb
        var rtRating = omdbResult.Ratings?.FirstOrDefault(r => r.Source == "Rotten Tomatoes");
        if (rtRating is not null && !string.IsNullOrWhiteSpace(rtRating.Value))
        {
            await UpsertRatingAsync(mediaItemId, ExternalProvider.RottenTomatoes, rtRating.Value, null);
        }
    }

    private async Task UpsertRatingAsync(int mediaItemId, ExternalProvider provider, string score, int? voteCount)
    {
        var existing = await _context.ExternalRatings
            .FirstOrDefaultAsync(r => r.MediaItemId == mediaItemId && r.Provider == provider);

        if (existing is not null)
        {
            existing.Score = score;
            existing.VoteCount = voteCount;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _context.ExternalRatings.Add(new ExternalRating
            {
                MediaItemId = mediaItemId,
                Provider = provider,
                Score = score,
                VoteCount = voteCount,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();
    }

    private async Task UpsertImagesAsync(int mediaItemId, int? seasonId, int? episodeId, ImageType imageType, List<TmdbImage>? images)
    {
        if (images is null || images.Count == 0) return;

        // Only keep English, Spanish, or language-neutral images
        var top = images
            .Where(i => i.Iso6391 is null or "en" or "es")
            .OrderBy(i => i.Iso6391 == "en" ? 0 : i.Iso6391 == "es" ? 1 : 2)
            .ThenByDescending(i => i.VoteAverage)
            .Take(20)
            .ToList();

        // Remove existing images of this type for this entity
        var existing = await _context.MediaImages
            .Where(i => i.MediaItemId == mediaItemId && i.SeasonId == seasonId && i.EpisodeId == episodeId && i.ImageType == imageType)
            .ToListAsync();

        _context.MediaImages.RemoveRange(existing);

        foreach (var img in top)
        {
            _context.MediaImages.Add(new MediaImage
            {
                MediaItemId = mediaItemId,
                SeasonId = seasonId,
                EpisodeId = episodeId,
                ImageType = imageType,
                RemoteUrl = $"https://image.tmdb.org/t/p/original{img.FilePath}",
                Width = img.Width,
                Height = img.Height,
                Language = img.Iso6391
            });
        }
    }

    private async Task LinkJellyfinItemAsync(string jellyfinItemId, string name, MediaType type, int mediaItemId)
    {
        var existing = await _context.JellyfinLibraryItems
            .FirstOrDefaultAsync(j => j.JellyfinItemId == jellyfinItemId);

        if (existing is not null)
        {
            existing.MediaItemId = mediaItemId;
            existing.Name = name;
        }
        else
        {
            _context.JellyfinLibraryItems.Add(new JellyfinLibraryItem
            {
                JellyfinItemId = jellyfinItemId,
                Name = name,
                Type = type,
                MediaItemId = mediaItemId
            });
        }

        await _context.SaveChangesAsync();
    }

    private static string? StripHtmlTags(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        return HtmlTagRegex().Replace(html, string.Empty).Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s*\((\d{4})\)\s*$")]
    private static partial Regex YearSuffixRegex();
}
