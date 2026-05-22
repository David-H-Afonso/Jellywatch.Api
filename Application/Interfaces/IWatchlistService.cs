using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Application.Interfaces;

public interface IWatchlistService
{
    Task<ServiceResult<WatchlistIndexDto>> GetWatchlistsAsync(int currentUserId, int? profileId);
    Task<ServiceResult<List<WatchlistUserOptionDto>>> GetUserOptionsAsync(int currentUserId, string? search, int? watchlistId);
    Task<ServiceResult<WatchlistDetailDto>> GetWatchlistAsync(int currentUserId, int id, int? profileId);
    Task<ServiceResult<WatchlistDetailDto>> CreateWatchlistAsync(int currentUserId, CreateWatchlistDto dto, int? profileId);
    Task<ServiceResult> UpdateWatchlistAsync(int currentUserId, int id, UpdateWatchlistDto dto);
    Task<ServiceResult> DeleteWatchlistAsync(int currentUserId, int id);
    Task<ServiceResult> LeaveWatchlistAsync(int currentUserId, int id);
    Task<ServiceResult> CompleteWatchlistAsync(int currentUserId, int id);
    Task<ServiceResult> AddItemAsync(int currentUserId, int watchlistId, AddWatchlistItemDto dto);
    Task<ServiceResult> UpdateItemAsync(int currentUserId, int watchlistId, int itemId, UpdateWatchlistItemDto dto);
    Task<ServiceResult> DeleteItemAsync(int currentUserId, int watchlistId, int itemId);
    Task<ServiceResult> ReorderItemsAsync(int currentUserId, int watchlistId, ReorderWatchlistItemsDto dto);
    Task<ServiceResult<WatchlistInvitationDto>> InviteMemberAsync(int currentUserId, int watchlistId, InviteWatchlistMemberDto dto);
    Task<ServiceResult> AcceptInvitationAsync(int currentUserId, int invitationId);
    Task<ServiceResult> RejectInvitationAsync(int currentUserId, int invitationId);
    Task<ServiceResult> UpdateMemberAsync(int currentUserId, int watchlistId, int memberId, UpdateWatchlistMemberDto dto);
    Task<ServiceResult> RemoveMemberAsync(int currentUserId, int watchlistId, int memberId);
    Task<ServiceResult> RequestAccessAsync(int currentUserId, int watchlistId, CreateWatchlistAccessRequestDto dto);
    Task<ServiceResult> ApproveAccessRequestAsync(int currentUserId, int requestId);
    Task<ServiceResult> RejectAccessRequestAsync(int currentUserId, int requestId);
    Task<ServiceResult> SetDefaultWatchlistAsync(int currentUserId, SetDefaultWatchlistDto dto);
}
