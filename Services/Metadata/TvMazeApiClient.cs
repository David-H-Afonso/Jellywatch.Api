using System.Net;
using System.Text.Json;
using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Services.Metadata;

public class TvMazeApiClient : ITvMazeApiClient
{
    private const string BaseUrl = "https://api.tvmaze.com";

    // TVmaze free tier: 20 requests per 10 seconds
    private static readonly SemaphoreSlim RateLimiter = new(20, 20);

    private readonly HttpClient _httpClient;
    private readonly ILogger<TvMazeApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TvMazeApiClient(
        IHttpClientFactory httpClientFactory,
        ILogger<TvMazeApiClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient("TvMazeClient");
        _logger = logger;
    }

    public async Task<List<TvMazeSearchResult>> SearchShowsAsync(string query)
    {
        var url = $"{BaseUrl}/search/shows?q={Uri.EscapeDataString(query)}";
        var result = await SendWithRateLimitAsync<List<TvMazeSearchResult>>(url);
        return result ?? new List<TvMazeSearchResult>();
    }

    public async Task<TvMazeShow?> GetShowAsync(int tvMazeId)
    {
        var url = $"{BaseUrl}/shows/{tvMazeId}";
        return await SendWithRateLimitAsync<TvMazeShow>(url);
    }

    public async Task<TvMazeShow?> LookupByImdbAsync(string imdbId)
    {
        var url = $"{BaseUrl}/lookup/shows?imdb={Uri.EscapeDataString(imdbId)}";
        return await SendWithRateLimitAsync<TvMazeShow>(url);
    }

    public async Task<TvMazeShow?> LookupByTvdbAsync(int tvdbId)
    {
        var url = $"{BaseUrl}/lookup/shows?thetvdb={tvdbId}";
        return await SendWithRateLimitAsync<TvMazeShow>(url);
    }

    public async Task<List<TvMazeEpisode>> GetEpisodesAsync(int tvMazeId)
    {
        var url = $"{BaseUrl}/shows/{tvMazeId}/episodes";
        var result = await SendWithRateLimitAsync<List<TvMazeEpisode>>(url);
        return result ?? new List<TvMazeEpisode>();
    }

    private async Task<T?> SendWithRateLimitAsync<T>(string url) where T : class
    {
        await RateLimiter.WaitAsync();
        try
        {
            // Release after 500ms to enforce ~20 req/10sec
            _ = Task.Delay(500).ContinueWith(_ => RateLimiter.Release());

            var response = await _httpClient.GetAsync(url);

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);
                _logger.LogWarning("TVmaze rate limited (429). Retrying after {RetryAfter}s", retryAfter.TotalSeconds);
                await Task.Delay(retryAfter);

                response = await _httpClient.GetAsync(url);
            }

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("TVmaze resource not found: {Url}", url);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<T>(content, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TVmaze request failed for URL: {Url}", url);
            return null;
        }
    }
}
