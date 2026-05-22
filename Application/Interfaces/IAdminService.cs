using Jellywatch.Api.Contracts;
using Jellywatch.Api.Common;

namespace Jellywatch.Api.Application.Interfaces;

public interface IAdminService
{
    Task<ServiceResult<List<UserDto>>> GetUsersAsync(int? currentUserId);
    Task<ServiceResult<object>> DeleteUserAsync(int? currentUserId, int id);
    Task<ServiceResult<List<ProfileDto>>> GetAllProfilesAsync(int? currentUserId);
    Task<ServiceResult<object>> GetJellyfinUsersAsync(int? currentUserId);
    Task<ServiceResult<ProfileDto>> AddProfileFromJellyfinAsync(int? currentUserId, AddProfileRequest request);
    Task<ServiceResult<PagedResult<ImportQueueItemDto>>> GetImportQueueAsync(int? currentUserId, int page, int pageSize);
    Task<ServiceResult<PagedResult<MediaLibraryItemDto>>> GetMediaLibraryAsync(int? currentUserId, int page, int pageSize);
    Task<ServiceResult> DeleteMediaItemAsync(int? currentUserId, int id);
    Task<ServiceResult<object>> RefreshMediaItemAsync(int? currentUserId, int id, RefreshMediaItemDto? dto);
    Task<ServiceResult<object>> GetPosterOptionsAsync(int? currentUserId, int id);
    Task<ServiceResult<object>> SelectPosterAsync(int? currentUserId, int id, SelectPosterDto dto);
    Task<ServiceResult<object>> GetLogoOptionsAsync(int? currentUserId, int id);
    Task<ServiceResult<object>> SelectLogoAsync(int? currentUserId, int id, SelectPosterDto dto);
    Task<ServiceResult<object>> RefreshAllMetadataAsync(int? currentUserId);
    Task<ServiceResult<object>> RefreshAllImagesAsync(int? currentUserId);
    Task<ServiceResult<object>> PurgeProfileMediaAsync(int? currentUserId, int profileId);
    Task<ServiceResult<object>> DeleteProfileAsync(int? currentUserId, int id);
    Task<ServiceResult<UserDto>> CreateUserForProfileAsync(int? currentUserId, int id);
    Task<ServiceResult<List<BlacklistedItemDto>>> GetBlacklistAsync(int? currentUserId);
    Task<ServiceResult<BlacklistedItemDto>> AddToBlacklistAsync(int? currentUserId, AddToBlacklistDto dto);
    Task<ServiceResult> RemoveFromBlacklistAsync(int? currentUserId, int id);
    Task<ServiceResult<List<AdminProfileBlockDto>>> GetAllProfileBlocksAsync(int? currentUserId);
}
