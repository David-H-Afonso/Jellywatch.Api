using System.Text.Json;
using Microsoft.Extensions.Options;
using Jellywatch.Api.Configuration;
using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Services.Metadata;

public class OmdbApiClient : IOmdbApiClient
{
    private const string BaseUrl = "https://www.omdbapi.com/";

    private readonly HttpClient _httpClient;
    private readonly OmdbSettings _settings;
    private readonly ILogger<OmdbApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OmdbApiClient(
        IHttpClientFactory httpClientFactory,
        IOptions<OmdbSettings> settings,
        ILogger<OmdbApiClient> logger)
    {
        _httpClient = httpClientFactory.CreateClient("OmdbClient");
        _settings = settings.Value;
        _logger = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.ApiKey);

    public async Task<OmdbResponse?> GetByImdbIdAsync(string imdbId)
    {
        if (!IsConfigured)
        {
            _logger.LogDebug("OMDb API key not configured — skipping request");
            return null;
        }

        if (string.IsNullOrWhiteSpace(imdbId))
            return null;

        try
        {
            var url = $"{BaseUrl}?apikey={_settings.ApiKey}&i={Uri.EscapeDataString(imdbId)}";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<OmdbResponse>(content, JsonOptions);

            if (result?.Response == "False")
            {
                _logger.LogDebug("OMDb returned no results for IMDb ID: {ImdbId}", imdbId);
                return null;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "OMDb request failed for IMDb ID: {ImdbId}", imdbId);
            return null;
        }
    }
}
