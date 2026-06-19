using System.ComponentModel.DataAnnotations;
using Jellywatch.Api.Domain.Enums;

namespace Jellywatch.Api.Contracts;

public class WatchlistIndexDto
{
    public List<WatchlistSummaryDto> Watchlists { get; set; } = new();
    public List<WatchlistInvitationDto> PendingInvitations { get; set; } = new();
    public List<WatchlistAccessRequestDto> IncomingAccessRequests { get; set; } = new();
    public int? DefaultWatchlistId { get; set; }
}

public class WatchlistSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverUrl { get; set; }
    public WatchlistState State { get; set; }
    public int OwnerUserId { get; set; }
    public string OwnerUsername { get; set; } = string.Empty;
    public WatchlistRole Role { get; set; }
    public WatchlistPermissionsDto Permissions { get; set; } = new();
    public int ItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class WatchlistDetailDto : WatchlistSummaryDto
{
    public List<WatchlistMemberDto> Members { get; set; } = new();
    public List<WatchlistItemDto> Items { get; set; } = new();
}

public class WatchlistPermissionsDto
{
    public bool CanAddItems { get; set; }
    public bool CanRemoveItems { get; set; }
    public bool CanReorderItems { get; set; }
    public bool CanUpdateItemStatus { get; set; }
    public bool CanInviteMembers { get; set; }
    public bool CanManageMembers { get; set; }
    public bool CanUpdateWatchlist { get; set; }
    public bool CanDeleteWatchlist { get; set; }
}

public class WatchlistMemberDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public WatchlistRole Role { get; set; }
    public WatchlistPermissionsDto Permissions { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class WatchlistUserOptionDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public bool IsMember { get; set; }
    public bool HasPendingInvitation { get; set; }
}

public class WatchlistItemDto
{
    public int Id { get; set; }
    public WatchlistItemType ItemType { get; set; }
    public int? MediaItemId { get; set; }
    public int? ChildWatchlistId { get; set; }
    public WatchlistStatus Status { get; set; }
    public int Position { get; set; }
    public int? AddedByUserId { get; set; }
    public string? AddedByUsername { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public WatchlistMediaItemDto? Media { get; set; }
    public WatchlistChildDto? ChildWatchlist { get; set; }
}

public class WatchlistMediaItemDto
{
    public int MediaItemId { get; set; }
    public MediaType MediaType { get; set; }
    public int? SeriesId { get; set; }
    public int? MovieId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? OriginalTitle { get; set; }
    public string? PosterPath { get; set; }
    public string? ReleaseDate { get; set; }
    public bool IsInProfile { get; set; }
    public bool IsBlacklisted { get; set; }
    public bool CanAddToProfile { get; set; }
}

public class WatchlistChildDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverUrl { get; set; }
    public WatchlistState State { get; set; }
    public bool HasFullAccess { get; set; }
    public bool CanRequestAccess { get; set; }
    public List<WatchlistItemDto> Items { get; set; } = new();
}

public class WatchlistInvitationDto
{
    public int Id { get; set; }
    public int WatchlistId { get; set; }
    public string WatchlistName { get; set; } = string.Empty;
    public string? WatchlistDescription { get; set; }
    public int InvitedByUserId { get; set; }
    public string InvitedByUsername { get; set; } = string.Empty;
    public WatchlistRole Role { get; set; }
    public WatchlistInvitationStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public WatchlistChildDto? Preview { get; set; }
}

public class WatchlistAccessRequestDto
{
    public int Id { get; set; }
    public int WatchlistId { get; set; }
    public string WatchlistName { get; set; } = string.Empty;
    public int RequestingUserId { get; set; }
    public string RequestingUsername { get; set; } = string.Empty;
    public WatchlistAccessRequestStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateWatchlistDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public WatchlistState State { get; set; } = WatchlistState.Pending;
    public AddWatchlistItemDto? InitialItem { get; set; }
}

public class UpdateWatchlistDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public WatchlistState State { get; set; }
}

public class AddWatchlistItemDto
{
    public WatchlistItemType ItemType { get; set; } = WatchlistItemType.MediaItem;
    public int? MediaItemId { get; set; }
    public int? ChildWatchlistId { get; set; }
    public WatchlistStatus Status { get; set; } = WatchlistStatus.WantToWatch;
    public int? Position { get; set; }
}

public class UpdateWatchlistItemDto
{
    public WatchlistStatus Status { get; set; }
    public int? Position { get; set; }
}

public class ReorderWatchlistItemsDto
{
    public List<int> ItemIds { get; set; } = new();
}

public class InviteWatchlistMemberDto
{
    public int? UserId { get; set; }
    public string? Username { get; set; }
    public WatchlistRole Role { get; set; } = WatchlistRole.Member;
    public WatchlistPermissionsDto? Permissions { get; set; }
    public string? Message { get; set; }
}

public class UpdateWatchlistMemberDto
{
    public WatchlistRole Role { get; set; } = WatchlistRole.Member;
    public WatchlistPermissionsDto Permissions { get; set; } = new();
}

public class CreateWatchlistAccessRequestDto
{
    public string? Message { get; set; }
}

public class SetDefaultWatchlistDto
{
    public int? WatchlistId { get; set; }
}

public class SetWatchlistCoverDto
{
    [Required(ErrorMessage = "URL is required")]
    [Url(ErrorMessage = "A valid URL is required")]
    public string Url { get; set; } = string.Empty;
}

// ━━━━ IMPORT / EXPORT ━━━━

public class WatchlistExportDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string State { get; set; } = string.Empty;
    public DateTime ExportedAt { get; set; }
    public List<WatchlistExportItemDto> Items { get; set; } = new();
}

public class WatchlistExportItemDto
{
    public string MediaType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Position { get; set; }
}

public class WatchlistImportDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? State { get; set; }
    public List<WatchlistImportItemDto> Items { get; set; } = new();
}

public class WatchlistImportItemDto
{
    public string MediaType { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int? TmdbId { get; set; }
    public string? ImdbId { get; set; }
    public string? Status { get; set; }
    public int? Position { get; set; }
}

public class WatchlistImportResultDto
{
    public int WatchlistId { get; set; }
    public string WatchlistName { get; set; } = string.Empty;
    public int TotalItems { get; set; }
    public int ImportedItems { get; set; }
    public int SkippedItems { get; set; }
    public List<string> Errors { get; set; } = new();
}
