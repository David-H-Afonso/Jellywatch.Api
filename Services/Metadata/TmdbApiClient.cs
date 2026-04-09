using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Jellywatch.Api.Configuration;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure;

namespace Jellywatch.Api.Services.Metadata;

public class TmdbApiClient : ITmdbApiClient
{
    private const string BaseUrl = "https://api.themoviedb.org/3";
    private const int MaxRequestsPerSecond = 40;
    private const int MaxRetries = 3;

    private readonly HttpClient _httpClient;
    private readonly TmdbSettings _settings;
    private readonly JellywatchDbContext _context;
    private readonly ILogger<TmdbApiClient> _logger;
    private readonly SemaphoreSlim _rateLimiter = new(MaxRequestsPerSecond, MaxRequestsPerSecond);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public TmdbApiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<TmdbSettings> settings,
        JellywatchDbContext context,
        ILogger<TmdbApiClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient("TmdbClient");
        _settings = settings.Value;
        _context = context;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.ApiKey);

    public async Task<List<TmdbTvSearchResult>> SearchTvAsync(string query, int? year = null)
    {
        var url = $"{BaseUrl}/search/tv?query={Uri.EscapeDataString(query)}&language={_settings.PrimaryLanguage}";
        if (year.HasValue)
            url += $"&first_air_date_year={year.Value}";

        var response = await SendWithRetryAsync<TmdbSearchResponse<TmdbTvSearchResult>>(url);
        return response?.Results ?? new List<TmdbTvSearchResult>();
    }

    public async Task<List<TmdbMovieSearchResult>> SearchMovieAsync(string query, int? year = null)
    {
        var url = $"{BaseUrl}/search/movie?query={Uri.EscapeDataString(query)}&language={_settings.PrimaryLanguage}";
        if (year.HasValue)
            url += $"&year={year.Value}";

        var response = await SendWithRetryAsync<TmdbSearchResponse<TmdbMovieSearchResult>>(url);
        return response?.Results ?? new List<TmdbMovieSearchResult>();
    }

    public async Task<TmdbTvDetails?> GetTvDetailsAsync(int tmdbId)
    {
        var cacheKey = $"tv-{tmdbId}";
        var cached = await GetCachedResponseAsync<TmdbTvDetails>(ExternalProvider.Tmdb, cacheKey);
        if (cached is not null) return cached;

        var url = $"{BaseUrl}/tv/{tmdbId}?language={_settings.PrimaryLanguage}&append_to_response=external_ids";
        var result = await SendWithRetryAsync<TmdbTvDetails>(url);

        if (result is not null)
            await CacheResponseAsync(ExternalProvider.Tmdb, cacheKey, result, TimeSpan.FromHours(_settings.CacheDetailsTtlHours));

        return result;
    }

    public async Task<TmdbSeasonDetails?> GetTvSeasonAsync(int tmdbId, int seasonNumber)
    {
        var cacheKey = $"tv-{tmdbId}-season-{seasonNumber}";
        var cached = await GetCachedResponseAsync<TmdbSeasonDetails>(ExternalProvider.Tmdb, cacheKey);
        if (cached is not null) return cached;

        var url = $"{BaseUrl}/tv/{tmdbId}/season/{seasonNumber}?language={_settings.PrimaryLanguage}";
        var result = await SendWithRetryAsync<TmdbSeasonDetails>(url);

        if (result is not null)
            await CacheResponseAsync(ExternalProvider.Tmdb, cacheKey, result, TimeSpan.FromHours(_settings.CacheDetailsTtlHours));

        return result;
    }

    public async Task<TmdbMovieDetails?> GetMovieDetailsAsync(int tmdbId)
    {
        var cacheKey = $"movie-{tmdbId}";
        var cached = await GetCachedResponseAsync<TmdbMovieDetails>(ExternalProvider.Tmdb, cacheKey);
        if (cached is not null) return cached;

        var url = $"{BaseUrl}/movie/{tmdbId}?language={_settings.PrimaryLanguage}&append_to_response=external_ids";
        var result = await SendWithRetryAsync<TmdbMovieDetails>(url);

        if (result is not null)
            await CacheResponseAsync(ExternalProvider.Tmdb, cacheKey, result, TimeSpan.FromHours(_settings.CacheDetailsTtlHours));

        return result;
    }

    public async Task<TmdbImageCollection?> GetImagesAsync(int tmdbId, string mediaType, bool forceRefresh = false)
    {
        var cacheKey = $"{mediaType}-{tmdbId}-images";

        if (!forceRefresh)
        {
            var cached = await GetCachedResponseAsync<TmdbImageCollection>(ExternalProvider.Tmdb, cacheKey);
            if (cached is not null) return cached;
        }
        else
        {
            // Invalidate stale cache entry so fresh data is always stored
            var stale = _context.ProviderCacheEntries
                .FirstOrDefault(c => c.Provider == ExternalProvider.Tmdb && c.ExternalId == cacheKey);
            if (stale is not null)
            {
                _context.ProviderCacheEntries.Remove(stale);
                await _context.SaveChangesAsync();
            }
        }

        var url = $"{BaseUrl}/{mediaType}/{tmdbId}/images?include_image_language=en,es,null";
        var result = await SendWithRetryAsync<TmdbImageCollection>(url);

        if (result is not null)
            await CacheResponseAsync(ExternalProvider.Tmdb, cacheKey, result, TimeSpan.FromDays(_settings.CacheImagesTtlDays));

        return result;
    }

    public async Task<TmdbTranslationsResponse?> GetTranslationsAsync(int tmdbId, string mediaType)
    {
        var cacheKey = $"{mediaType}-{tmdbId}-translations";
        var cached = await GetCachedResponseAsync<TmdbTranslationsResponse>(ExternalProvider.Tmdb, cacheKey);
        if (cached is not null) return cached;

        var url = $"{BaseUrl}/{mediaType}/{tmdbId}/translations";
        var result = await SendWithRetryAsync<TmdbTranslationsResponse>(url);

        if (result is not null)
            await CacheResponseAsync(ExternalProvider.Tmdb, cacheKey, result, TimeSpan.FromHours(_settings.CacheDetailsTtlHours));

        return result;
    }

    public async Task<TmdbAggregateCreditsResponse?> GetTvAggregateCreditsAsync(int tmdbId)
    {
        var cacheKey = $"tv-{tmdbId}-aggregate-credits";
        var cached = await GetCachedResponseAsync<TmdbAggregateCreditsResponse>(ExternalProvider.Tmdb, cacheKey);
        if (cached is not null) return cached;

        var url = $"{BaseUrl}/tv/{tmdbId}/aggregate_credits?language={_settings.PrimaryLanguage}";
        var result = await SendWithRetryAsync<TmdbAggregateCreditsResponse>(url);

        if (result is not null)
            await CacheResponseAsync(ExternalProvider.Tmdb, cacheKey, result, TimeSpan.FromHours(_settings.CacheDetailsTtlHours));

        return result;
    }

    public async Task<TmdbCreditsResponse?> GetMovieCreditsAsync(int tmdbId)
    {
        var cacheKey = $"movie-{tmdbId}-credits";
        var cached = await GetCachedResponseAsync<TmdbCreditsResponse>(ExternalProvider.Tmdb, cacheKey);
        if (cached is not null) return cached;

        var url = $"{BaseUrl}/movie/{tmdbId}/credits?language={_settings.PrimaryLanguage}";
        var result = await SendWithRetryAsync<TmdbCreditsResponse>(url);

        if (result is not null)
            await CacheResponseAsync(ExternalProvider.Tmdb, cacheKey, result, TimeSpan.FromHours(_settings.CacheDetailsTtlHours));

        return result;
    }

    public async Task<TmdbPersonCreditsResponse?> GetPersonCombinedCreditsAsync(int personId)
    {
        var cacheKey = $"person-{personId}-combined-credits";
        var cached = await GetCachedResponseAsync<TmdbPersonCreditsResponse>(ExternalProvider.Tmdb, cacheKey);
        if (cached is not null) return cached;

        var url = $"{BaseUrl}/person/{personId}/combined_credits?language={_settings.PrimaryLanguage}";
        var result = await SendWithRetryAsync<TmdbPersonCreditsResponse>(url);

        if (result is not null)
            await CacheResponseAsync(ExternalProvider.Tmdb, cacheKey, result, TimeSpan.FromHours(_settings.CacheDetailsTtlHours));

        return result;
    }

    public async Task<TmdbPersonDetails?> GetPersonDetailsAsync(int personId)
    {
        var cacheKey = $"person-{personId}-details";
        var cached = await GetCachedResponseAsync<TmdbPersonDetails>(ExternalProvider.Tmdb, cacheKey);
        if (cached is not null) return cached;

        var url = $"{BaseUrl}/person/{personId}?language={_settings.PrimaryLanguage}";
        var result = await SendWithRetryAsync<TmdbPersonDetails>(url);

        if (result is not null)
            await CacheResponseAsync(ExternalProvider.Tmdb, cacheKey, result, TimeSpan.FromHours(_settings.CacheDetailsTtlHours));

        return result;
    }

    private async Task<T?> SendWithRetryAsync<T>(string url) where T : class
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("TMDB API key is not configured — skipping request");
            return null;
        }

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            await _rateLimiter.WaitAsync();
            try
            {
                // Release the rate limiter after a delay to enforce requests-per-second
                _ = Task.Delay(TimeSpan.FromMilliseconds(1000.0 / MaxRequestsPerSecond))
                        .ContinueWith(_ => _rateLimiter.Release());

                // TMDB v3 API key must be passed as query param, not Bearer header
                var separator = url.Contains('?') ? '&' : '?';
                var authenticatedUrl = $"{url}{separator}api_key={Uri.EscapeDataString(_settings.ApiKey!)}";
                var request = new HttpRequestMessage(HttpMethod.Get, authenticatedUrl);
                request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                    _logger.LogWarning("TMDB rate limited (429). Retrying after {RetryAfter}s (attempt {Attempt}/{MaxRetries})",
                        retryAfter.TotalSeconds, attempt + 1, MaxRetries);
                    await Task.Delay(retryAfter);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("TMDB resource not found: {Url}", url);
                    return null;
                }

                if ((int)response.StatusCode >= 500)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                    _logger.LogWarning("TMDB server error ({StatusCode}). Retrying after {Delay}s (attempt {Attempt}/{MaxRetries})",
                        (int)response.StatusCode, delay.TotalSeconds, attempt + 1, MaxRetries);
                    await Task.Delay(delay);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(content, JsonOptions);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                _logger.LogWarning(ex, "TMDB request failed. Retrying after {Delay}s (attempt {Attempt}/{MaxRetries})",
                    delay.TotalSeconds, attempt + 1, MaxRetries);
                await Task.Delay(delay);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TMDB request failed for URL: {Url}", url);
                return null;
            }
        }

        _logger.LogError("TMDB request failed after {MaxRetries} retries: {Url}", MaxRetries, url);
        return null;
    }

    private async Task<T?> GetCachedResponseAsync<T>(ExternalProvider provider, string externalId) where T : class
    {
        var entry = _context.ProviderCacheEntries
            .FirstOrDefault(c => c.Provider == provider && c.ExternalId == externalId && c.ExpiresAt > DateTime.UtcNow);

        if (entry?.ResponseJson is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<T>(entry.ResponseJson, JsonOptions);
        }
        catch (JsonException)
        {
            _context.ProviderCacheEntries.Remove(entry);
            await _context.SaveChangesAsync();
            return null;
        }
    }

    private async Task CacheResponseAsync(ExternalProvider provider, string externalId, object response, TimeSpan ttl)
    {
        var json = JsonSerializer.Serialize(response, JsonOptions);

        var existing = _context.ProviderCacheEntries
            .FirstOrDefault(c => c.Provider == provider && c.ExternalId == externalId);

        if (existing is not null)
        {
            existing.ResponseJson = json;
            existing.CachedAt = DateTime.UtcNow;
            existing.ExpiresAt = DateTime.UtcNow.Add(ttl);
        }
        else
        {
            _context.ProviderCacheEntries.Add(new ProviderCacheEntry
            {
                Provider = provider,
                ExternalId = externalId,
                ResponseJson = json,
                CachedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(ttl)
            });
        }

        await _context.SaveChangesAsync();
    }
}
