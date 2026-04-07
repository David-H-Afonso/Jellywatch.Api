using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Jellywatch.Api.Configuration;

namespace Jellywatch.Api.Services.Jellyfin;

public class JellyfinApiClient : IJellyfinApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JellyfinSettings _settings;
    private readonly ILogger<JellyfinApiClient> _logger;
    private string? _serverUrl;
    private string? _accessToken;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public JellyfinApiClient(IHttpClientFactory httpClientFactory, IOptions<JellyfinSettings> settings, ILogger<JellyfinApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _logger = logger;
        _serverUrl = _settings.BaseUrl;
    }

    public async Task<JellyfinAuthResult?> AuthenticateAsync(string serverUrl, string username, string password)
    {
        var client = _httpClientFactory.CreateClient("JellyfinClient");
        client.BaseAddress = new Uri(serverUrl.TrimEnd('/'));
        client.DefaultRequestHeaders.Add("X-Emby-Authorization",
            "MediaBrowser Client=\"Jellywatch\", Device=\"Server\", DeviceId=\"jellywatch-api\", Version=\"0.1.0\"");

        var payload = JsonSerializer.Serialize(new { Username = username, Pw = password });
        var content = new StringContent(payload, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/Users/AuthenticateByName", content);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Jellyfin authentication failed for user {Username} with status {Status}", username, response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var userId = root.GetProperty("User").GetProperty("Id").GetString() ?? string.Empty;
        var isAdmin = root.GetProperty("User").GetProperty("Policy").GetProperty("IsAdministrator").GetBoolean();
        var accessToken = root.GetProperty("AccessToken").GetString() ?? string.Empty;

        _serverUrl = serverUrl.TrimEnd('/');
        _accessToken = accessToken;

        string? avatarUrl = null;
        if (root.GetProperty("User").TryGetProperty("PrimaryImageTag", out var imageTag) && imageTag.GetString() != null)
        {
            avatarUrl = $"{_serverUrl}/Users/{userId}/Images/Primary?tag={imageTag.GetString()}";
        }

        return new JellyfinAuthResult
        {
            UserId = userId,
            Username = root.GetProperty("User").GetProperty("Name").GetString() ?? username,
            IsAdmin = isAdmin,
            AccessToken = accessToken,
            AvatarUrl = avatarUrl
        };
    }

    public async Task<List<JellyfinUserInfo>> GetUsersAsync()
    {
        var client = CreateAuthenticatedClient();
        var response = await client.GetAsync("/Users");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.EnumerateArray().Select(u => new JellyfinUserInfo
        {
            Id = u.GetProperty("Id").GetString() ?? string.Empty,
            Name = u.GetProperty("Name").GetString() ?? string.Empty,
            IsAdministrator = u.TryGetProperty("Policy", out var policy) && policy.GetProperty("IsAdministrator").GetBoolean(),
            PrimaryImageTag = u.TryGetProperty("PrimaryImageTag", out var tag) ? tag.GetString() : null
        }).ToList();
    }

    public async Task<List<JellyfinLibraryInfo>> GetLibrariesAsync(string userId)
    {
        var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/Users/{userId}/Views");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.GetProperty("Items").EnumerateArray().Select(item => new JellyfinLibraryInfo
        {
            Id = item.GetProperty("Id").GetString() ?? string.Empty,
            Name = item.GetProperty("Name").GetString() ?? string.Empty,
            CollectionType = item.TryGetProperty("CollectionType", out var ct) ? ct.GetString() : null
        }).ToList();
    }

    public async Task<List<JellyfinItemInfo>> GetItemsAsync(string userId, string? parentId = null, string? itemTypes = null, int? startIndex = null, int? limit = null)
    {
        var client = CreateAuthenticatedClient();
        var query = $"/Users/{userId}/Items?Recursive=true&Fields=Overview,ProviderIds,PremiereDate,RunTimeTicks";
        if (parentId != null) query += $"&ParentId={parentId}";
        if (itemTypes != null) query += $"&IncludeItemTypes={itemTypes}";
        if (startIndex.HasValue) query += $"&StartIndex={startIndex}";
        if (limit.HasValue) query += $"&Limit={limit}";

        var response = await client.GetAsync(query);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        return doc.RootElement.GetProperty("Items").EnumerateArray().Select(ParseItemInfo).ToList();
    }

    public async Task<JellyfinItemInfo?> GetItemAsync(string itemId, string userId)
    {
        var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/Users/{userId}/Items/{itemId}?Fields=Overview,ProviderIds,PremiereDate,RunTimeTicks");
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return ParseItemInfo(doc.RootElement);
    }

    public async Task<JellyfinItemInfo?> GetItemAsync(string itemId)
    {
        var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/Items/{itemId}?Fields=Overview,ProviderIds,PremiereDate,RunTimeTicks");
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return ParseItemInfo(doc.RootElement);
    }

    public async Task<JellyfinUserData?> GetUserDataAsync(string userId, string itemId)
    {
        var client = CreateAuthenticatedClient();
        var response = await client.GetAsync($"/Users/{userId}/Items/{itemId}?Fields=UserData");
        if (!response.IsSuccessStatusCode) return null;

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("UserData", out var userData)) return null;

        return new JellyfinUserData
        {
            Played = userData.TryGetProperty("Played", out var played) && played.GetBoolean(),
            PlaybackPositionTicks = userData.TryGetProperty("PlaybackPositionTicks", out var pos) ? pos.GetInt64() : 0,
            PlayCount = userData.TryGetProperty("PlayCount", out var count) ? count.GetInt32() : 0,
            IsFavorite = userData.TryGetProperty("IsFavorite", out var fav) && fav.GetBoolean(),
            PlayedPercentage = userData.TryGetProperty("PlayedPercentage", out var pct) ? pct.GetDouble() : null,
            LastPlayedDate = userData.TryGetProperty("LastPlayedDate", out var lpd) && lpd.ValueKind != JsonValueKind.Null
                ? lpd.GetDateTime() : null
        };
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _httpClientFactory.CreateClient("JellyfinClient");
        if (!string.IsNullOrEmpty(_serverUrl))
            client.BaseAddress = new Uri(_serverUrl);

        if (!string.IsNullOrEmpty(_settings.ApiKey))
            client.DefaultRequestHeaders.Add("X-Emby-Token", _settings.ApiKey);
        else if (!string.IsNullOrEmpty(_accessToken))
            client.DefaultRequestHeaders.Add("X-Emby-Token", _accessToken);

        return client;
    }

    private static JellyfinItemInfo ParseItemInfo(JsonElement item)
    {
        var info = new JellyfinItemInfo
        {
            Id = item.GetProperty("Id").GetString() ?? string.Empty,
            Name = item.GetProperty("Name").GetString() ?? string.Empty,
            Type = item.TryGetProperty("Type", out var type) ? type.GetString() : null,
            SeriesId = item.TryGetProperty("SeriesId", out var sid) ? sid.GetString() : null,
            SeriesName = item.TryGetProperty("SeriesName", out var sname) ? sname.GetString() : null,
            SeasonId = item.TryGetProperty("SeasonId", out var seid) ? seid.GetString() : null,
            IndexNumber = item.TryGetProperty("IndexNumber", out var idx) ? idx.GetInt32() : null,
            ParentIndexNumber = item.TryGetProperty("ParentIndexNumber", out var pidx) ? pidx.GetInt32() : null,
            Overview = item.TryGetProperty("Overview", out var overview) ? overview.GetString() : null,
            PremiereDate = item.TryGetProperty("PremiereDate", out var premiere) ? premiere.GetString() : null,
            RunTimeTicks = item.TryGetProperty("RunTimeTicks", out var runtime) ? runtime.GetInt64() : null
        };

        if (item.TryGetProperty("ProviderIds", out var providerIds))
        {
            info.ProviderIds = new Dictionary<string, string>();
            foreach (var prop in providerIds.EnumerateObject())
            {
                var val = prop.Value.GetString();
                if (val != null) info.ProviderIds[prop.Name] = val;
            }
        }

        if (item.TryGetProperty("UserData", out var userData))
        {
            info.UserData = new JellyfinUserData
            {
                Played = userData.TryGetProperty("Played", out var played) && played.GetBoolean(),
                PlaybackPositionTicks = userData.TryGetProperty("PlaybackPositionTicks", out var pos) ? pos.GetInt64() : 0,
                PlayCount = userData.TryGetProperty("PlayCount", out var count) ? count.GetInt32() : 0,
                IsFavorite = userData.TryGetProperty("IsFavorite", out var fav) && fav.GetBoolean(),
                PlayedPercentage = userData.TryGetProperty("PlayedPercentage", out var pct) ? pct.GetDouble() : null,
                LastPlayedDate = userData.TryGetProperty("LastPlayedDate", out var lpd) && lpd.ValueKind != JsonValueKind.Null
                    ? lpd.GetDateTime() : null
            };
        }

        return info;
    }
}
