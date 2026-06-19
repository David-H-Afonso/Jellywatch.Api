using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Jellywatch.Api.Configuration;

namespace Jellywatch.Api.Infrastructure.ExternalServices;

public class RadarrApiClient : IArrAvailabilityClient
{
    private readonly HttpClient _httpClient;
    private readonly RadarrSettings _settings;
    private readonly ILogger<RadarrApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RadarrApiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<RadarrSettings> settings,
        ILogger<RadarrApiClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient("RadarrClient");
        _settings = settings.Value;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_settings.ApiKey);

    public async Task<ArrMediaStatus?> GetMovieStatusAsync(int tmdbId)
    {
        if (!IsConfigured) return null;

        try
        {
            var url = $"{_settings.BaseUrl!.TrimEnd('/')}/api/v3/movie?tmdbId={tmdbId}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("X-Api-Key", _settings.ApiKey);

            using var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var movies = JsonSerializer.Deserialize<List<RadarrMovieResponse>>(json, JsonOptions);

            if (movies is null || movies.Count == 0)
                return null;

            var movie = movies[0];
            return new ArrMediaStatus(
                IsMonitored: movie.Monitored,
                HasFile: movie.HasFile,
                Title: movie.Title,
                Status: movie.Status,
                QualityProfileName: null,
                SizeOnDisk: movie.SizeOnDisk > 0 ? (int)(movie.SizeOnDisk / (1024 * 1024)) : null,
                EpisodeFileCount: null,
                TotalEpisodeCount: null
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query Radarr for tmdbId={TmdbId}", tmdbId);
            return null;
        }
    }

    public Task<ArrMediaStatus?> GetSeriesStatusAsync(int tvdbId)
    {
        // Radarr does not handle series
        return Task.FromResult<ArrMediaStatus?>(null);
    }

    private sealed class RadarrMovieResponse
    {
        public string? Title { get; set; }
        public bool Monitored { get; set; }
        public bool HasFile { get; set; }
        public string? Status { get; set; }
        public long SizeOnDisk { get; set; }
    }
}
