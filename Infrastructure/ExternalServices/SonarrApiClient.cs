using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Jellywatch.Api.Configuration;

namespace Jellywatch.Api.Infrastructure.ExternalServices;

public class SonarrApiClient : IArrAvailabilityClient
{
    private readonly HttpClient _httpClient;
    private readonly SonarrSettings _settings;
    private readonly ILogger<SonarrApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SonarrApiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<SonarrSettings> settings,
        ILogger<SonarrApiClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient("SonarrClient");
        _settings = settings.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_settings.ApiKey);

    public Task<ArrMediaStatus?> GetMovieStatusAsync(int tmdbId)
    {
        // Sonarr does not handle movies
        return Task.FromResult<ArrMediaStatus?>(null);
    }

    public async Task<ArrMediaStatus?> GetSeriesStatusAsync(int tvdbId)
    {
        if (!IsConfigured) return null;

        try
        {
            var url = $"{_settings.BaseUrl!.TrimEnd('/')}/api/v3/series?tvdbId={tvdbId}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", _settings.ApiKey);

            using var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var seriesList = JsonSerializer.Deserialize<List<SonarrSeriesResponse>>(json, JsonOptions);

            if (seriesList is null || seriesList.Count == 0)
                return null;

            var series = seriesList[0];
            var stats = series.Statistics;

            return new ArrMediaStatus(
                IsMonitored: series.Monitored,
                HasFile: (stats?.EpisodeFileCount ?? 0) > 0,
                Title: series.Title,
                Status: series.Status,
                QualityProfileName: null,
                SizeOnDisk: stats?.SizeOnDisk > 0 ? (int)(stats.SizeOnDisk / (1024 * 1024)) : null,
                EpisodeFileCount: stats?.EpisodeFileCount,
                TotalEpisodeCount: stats?.TotalEpisodeCount
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Sonarr for tvdbId={TvdbId}", tvdbId);
            return null;
        }
    }

    private sealed class SonarrSeriesResponse
    {
        public string? Title { get; set; }
        public bool Monitored { get; set; }
        public string? Status { get; set; }
        public SonarrStatistics? Statistics { get; set; }
    }

    private sealed class SonarrStatistics
    {
        public int EpisodeFileCount { get; set; }
        public int TotalEpisodeCount { get; set; }
        public long SizeOnDisk { get; set; }
    }
}
