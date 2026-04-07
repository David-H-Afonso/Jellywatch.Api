namespace Jellywatch.Api.Services.Jellyfin;

public class JellyfinAuthResult
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
}

public interface IJellyfinApiClient
{
    Task<JellyfinAuthResult?> AuthenticateAsync(string serverUrl, string username, string password);
    Task<List<JellyfinUserInfo>> GetUsersAsync();
    Task<List<JellyfinLibraryInfo>> GetLibrariesAsync(string userId);
    Task<List<JellyfinItemInfo>> GetItemsAsync(string userId, string? parentId = null, string? itemTypes = null, int? startIndex = null, int? limit = null);
    Task<JellyfinItemInfo?> GetItemAsync(string itemId, string userId);
    Task<JellyfinItemInfo?> GetItemAsync(string itemId);
    Task<JellyfinUserData?> GetUserDataAsync(string userId, string itemId);
}

public class JellyfinUserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsAdministrator { get; set; }
    public string? PrimaryImageTag { get; set; }
}

public class JellyfinLibraryInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CollectionType { get; set; }
}

public class JellyfinItemInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? SeriesId { get; set; }
    public string? SeriesName { get; set; }
    public string? SeasonId { get; set; }
    public int? IndexNumber { get; set; }
    public int? ParentIndexNumber { get; set; }
    public string? Overview { get; set; }
    public string? PremiereDate { get; set; }
    public long? RunTimeTicks { get; set; }
    public JellyfinUserData? UserData { get; set; }
    public Dictionary<string, string>? ProviderIds { get; set; }
    public string? ImageTags { get; set; }
}

public class JellyfinUserData
{
    public bool Played { get; set; }
    public long PlaybackPositionTicks { get; set; }
    public int PlayCount { get; set; }
    public bool IsFavorite { get; set; }
    public double? PlayedPercentage { get; set; }
    public DateTime? LastPlayedDate { get; set; }
}
