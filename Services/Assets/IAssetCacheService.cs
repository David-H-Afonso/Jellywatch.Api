namespace Jellywatch.Api.Services.Assets;

public interface IAssetCacheService
{
    Task<string?> GetOrDownloadAsync(int mediaItemId, string imageType, string remoteUrl);
    Task<string?> GetLocalPathAsync(int mediaItemId, string imageType);
    Task RefreshAsync(int mediaItemId);
    Task SelectImageAsync(int mediaItemId, string imageType, string remoteUrl);
    string GetAssetDirectory();
}
