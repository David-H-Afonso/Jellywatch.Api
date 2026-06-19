using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure.Persistence;

namespace Jellywatch.Api.Application.Services;

public class WatchlistService : IWatchlistService
{
    private const int MaxNestedDepth = 5;
    private readonly JellywatchDbContext _context;
    private readonly IMetadataResolutionService _metadata;
    private readonly ILogger<WatchlistService> _logger;

    public WatchlistService(JellywatchDbContext context, IMetadataResolutionService metadata, ILogger<WatchlistService> logger)
    {
        _context = context;
        _metadata = metadata;
        _logger = logger;
    }

    public async Task<ServiceResult<WatchlistIndexDto>> GetWatchlistsAsync(int currentUserId, int? profileId)
    {
        var members = await _context.WatchlistMembers
            .Include(m => m.Watchlist)
                .ThenInclude(w => w.OwnerUser)
            .Include(m => m.Watchlist)
                .ThenInclude(w => w.Items)
            .Where(m => m.UserId == currentUserId)
            .OrderBy(m => m.Watchlist.Name)
            .ToListAsync();

        var summaries = members.Select(m => MapSummary(m.Watchlist, m)).ToList();

        var invitations = await _context.WatchlistInvitations
            .Include(i => i.Watchlist)
                .ThenInclude(w => w.OwnerUser)
            .Include(i => i.InvitedByUser)
            .Where(i => i.InvitedUserId == currentUserId && i.Status == WatchlistInvitationStatus.Pending)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        var incomingRequests = await _context.WatchlistAccessRequests
            .Include(r => r.Watchlist)
            .Include(r => r.RequestingUser)
            .Where(r => r.Status == WatchlistAccessRequestStatus.Pending
                && r.Watchlist.Members.Any(m => m.UserId == currentUserId
                    && (m.Role == WatchlistRole.Owner || m.Role == WatchlistRole.Admin || m.CanManageMembers)))
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new WatchlistAccessRequestDto
            {
                Id = r.Id,
                WatchlistId = r.WatchlistId,
                WatchlistName = r.Watchlist.Name,
                RequestingUserId = r.RequestingUserId,
                RequestingUsername = r.RequestingUser.Username,
                Status = r.Status,
                CreatedAt = r.CreatedAt
            })
            .ToListAsync();

        var preference = await _context.UserWatchlistPreferences
            .FirstOrDefaultAsync(p => p.UserId == currentUserId);

        var invitationDtos = new List<WatchlistInvitationDto>();
        foreach (var invitation in invitations)
        {
            invitationDtos.Add(new WatchlistInvitationDto
            {
                Id = invitation.Id,
                WatchlistId = invitation.WatchlistId,
                WatchlistName = invitation.Watchlist.Name,
                WatchlistDescription = invitation.Watchlist.Description,
                InvitedByUserId = invitation.InvitedByUserId,
                InvitedByUsername = invitation.InvitedByUser.Username,
                Role = invitation.Role,
                Status = invitation.Status,
                CreatedAt = invitation.CreatedAt,
                Preview = await MapChildWatchlistAsync(invitation.WatchlistId, currentUserId, profileId, 0, false)
            });
        }

        return ServiceResult<WatchlistIndexDto>.Ok(new WatchlistIndexDto
        {
            Watchlists = summaries,
            PendingInvitations = invitationDtos,
            IncomingAccessRequests = incomingRequests,
            DefaultWatchlistId = preference?.DefaultWatchlistId
        });
    }

    public async Task<ServiceResult<List<WatchlistUserOptionDto>>> GetUserOptionsAsync(int currentUserId, string? search, int? watchlistId)
    {
        var normalizedSearch = search?.Trim().ToLower();
        var query = _context.Users
            .Where(u => u.Id != currentUserId);

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
            query = query.Where(u => u.Username.ToLower().Contains(normalizedSearch));

        var users = await query
            .OrderBy(u => u.Username)
            .Take(20)
            .Select(u => new WatchlistUserOptionDto
            {
                Id = u.Id,
                Username = u.Username
            })
            .ToListAsync();

        if (watchlistId.HasValue && users.Count > 0)
        {
            var userIds = users.Select(u => u.Id).ToList();
            var memberIds = await _context.WatchlistMembers
                .Where(m => m.WatchlistId == watchlistId.Value && userIds.Contains(m.UserId))
                .Select(m => m.UserId)
                .ToListAsync();
            var pendingInvitationIds = await _context.WatchlistInvitations
                .Where(i => i.WatchlistId == watchlistId.Value
                    && userIds.Contains(i.InvitedUserId)
                    && i.Status == WatchlistInvitationStatus.Pending)
                .Select(i => i.InvitedUserId)
                .ToListAsync();

            var memberSet = memberIds.ToHashSet();
            var pendingSet = pendingInvitationIds.ToHashSet();
            foreach (var option in users)
            {
                option.IsMember = memberSet.Contains(option.Id);
                option.HasPendingInvitation = pendingSet.Contains(option.Id);
            }
        }

        return ServiceResult<List<WatchlistUserOptionDto>>.Ok(users);
    }

    public async Task<ServiceResult<WatchlistDetailDto>> GetWatchlistAsync(int currentUserId, int id, int? profileId)
    {
        var member = await GetMemberAsync(id, currentUserId);
        if (member is null) return ServiceResult<WatchlistDetailDto>.Fail("Forbidden", 403);

        var watchlist = await LoadWatchlistAsync(id);
        if (watchlist is null) return ServiceResult<WatchlistDetailDto>.Fail("Watchlist not found", 404);

        return ServiceResult<WatchlistDetailDto>.Ok(await MapDetailAsync(watchlist, member, currentUserId, profileId));
    }

    public async Task<ServiceResult<WatchlistDetailDto>> CreateWatchlistAsync(int currentUserId, CreateWatchlistDto dto, int? profileId)
    {
        var name = dto.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return ServiceResult<WatchlistDetailDto>.Fail("Name is required", 400);

        var watchlist = new Watchlist
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            OwnerUserId = currentUserId,
            State = dto.State
        };

        _context.Watchlists.Add(watchlist);
        await _context.SaveChangesAsync();

        _context.WatchlistMembers.Add(new WatchlistMember
        {
            WatchlistId = watchlist.Id,
            UserId = currentUserId,
            Role = WatchlistRole.Owner,
            CanAddItems = true,
            CanRemoveItems = true,
            CanReorderItems = true,
            CanUpdateItemStatus = true,
            CanInviteMembers = true,
            CanManageMembers = true,
            CanUpdateWatchlist = true
        });

        await _context.SaveChangesAsync();

        if (dto.InitialItem is not null)
        {
            var itemResult = await AddItemInternalAsync(watchlist.Id, dto.InitialItem, currentUserId);
            if (itemResult is not null) return ServiceResult<WatchlistDetailDto>.Fail(itemResult.Error!, itemResult.StatusCode ?? 400);
        }

        var created = await LoadWatchlistAsync(watchlist.Id);
        var member = await GetMemberAsync(watchlist.Id, currentUserId);
        return ServiceResult<WatchlistDetailDto>.Ok(await MapDetailAsync(created!, member, currentUserId, profileId));
    }

    public async Task<ServiceResult> UpdateWatchlistAsync(int currentUserId, int id, UpdateWatchlistDto dto)
    {
        var member = await GetMemberAsync(id, currentUserId);
        if (member is null || !EffectivePermissions(member).CanUpdateWatchlist) return ServiceResult.Fail("Forbidden", 403);

        var watchlist = await _context.Watchlists.FindAsync(id);
        if (watchlist is null) return ServiceResult.Fail("Watchlist not found", 404);

        var name = dto.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return ServiceResult.Fail("Name is required", 400);

        watchlist.Name = name;
        watchlist.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        watchlist.State = dto.State;
        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteWatchlistAsync(int currentUserId, int id)
    {
        var member = await GetMemberAsync(id, currentUserId);
        if (member is null || member.Role != WatchlistRole.Owner) return ServiceResult.Fail("Forbidden", 403);

        var watchlist = await _context.Watchlists.FindAsync(id);
        if (watchlist is null) return ServiceResult.Fail("Watchlist not found", 404);

        var references = await _context.WatchlistItems
            .Where(i => i.ChildWatchlistId == id)
            .ToListAsync();
        _context.WatchlistItems.RemoveRange(references);

        var preferences = await _context.UserWatchlistPreferences
            .Where(p => p.DefaultWatchlistId == id)
            .ToListAsync();
        foreach (var preference in preferences)
            preference.DefaultWatchlistId = null;

        _context.Watchlists.Remove(watchlist);
        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> LeaveWatchlistAsync(int currentUserId, int id)
    {
        var member = await _context.WatchlistMembers
            .FirstOrDefaultAsync(m => m.WatchlistId == id && m.UserId == currentUserId);
        if (member is null) return ServiceResult.Fail("Watchlist membership not found", 404);
        if (member.Role == WatchlistRole.Owner)
            return ServiceResult.Fail("Owner cannot leave their own watchlist. Delete it or transfer ownership first.", 400);

        _context.WatchlistMembers.Remove(member);

        var preference = await _context.UserWatchlistPreferences
            .FirstOrDefaultAsync(p => p.UserId == currentUserId && p.DefaultWatchlistId == id);
        if (preference is not null)
            preference.DefaultWatchlistId = null;

        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> CompleteWatchlistAsync(int currentUserId, int id)
    {
        var member = await GetMemberAsync(id, currentUserId);
        if (member is null || !EffectivePermissions(member).CanUpdateWatchlist) return ServiceResult.Fail("Forbidden", 403);

        var watchlist = await _context.Watchlists.FindAsync(id);
        if (watchlist is null) return ServiceResult.Fail("Watchlist not found", 404);

        watchlist.State = WatchlistState.Completed;
        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> AddItemAsync(int currentUserId, int watchlistId, AddWatchlistItemDto dto)
    {
        var member = await GetMemberAsync(watchlistId, currentUserId);
        if (member is null || !EffectivePermissions(member).CanAddItems) return ServiceResult.Fail("Forbidden", 403);

        var itemResult = await AddItemInternalAsync(watchlistId, dto, currentUserId);
        if (itemResult is not null) return itemResult;

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UpdateItemAsync(int currentUserId, int watchlistId, int itemId, UpdateWatchlistItemDto dto)
    {
        var member = await GetMemberAsync(watchlistId, currentUserId);
        if (member is null) return ServiceResult.Fail("Forbidden", 403);

        var permissions = EffectivePermissions(member);
        if (!permissions.CanUpdateItemStatus && !permissions.CanReorderItems) return ServiceResult.Fail("Forbidden", 403);

        var item = await _context.WatchlistItems.FirstOrDefaultAsync(i => i.Id == itemId && i.WatchlistId == watchlistId);
        if (item is null) return ServiceResult.Fail("Watchlist item not found", 404);

        if (permissions.CanUpdateItemStatus)
            item.Status = dto.Status;

        if (dto.Position.HasValue)
        {
            if (!permissions.CanReorderItems) return ServiceResult.Fail("Forbidden", 403);
            await MoveItemAsync(watchlistId, item, dto.Position.Value);
        }

        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> DeleteItemAsync(int currentUserId, int watchlistId, int itemId)
    {
        var member = await GetMemberAsync(watchlistId, currentUserId);
        if (member is null || !EffectivePermissions(member).CanRemoveItems) return ServiceResult.Fail("Forbidden", 403);

        var item = await _context.WatchlistItems.FirstOrDefaultAsync(i => i.Id == itemId && i.WatchlistId == watchlistId);
        if (item is null) return ServiceResult.Fail("Watchlist item not found", 404);

        _context.WatchlistItems.Remove(item);
        await _context.SaveChangesAsync();
        await NormalizePositionsAsync(watchlistId);

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ReorderItemsAsync(int currentUserId, int watchlistId, ReorderWatchlistItemsDto dto)
    {
        var member = await GetMemberAsync(watchlistId, currentUserId);
        if (member is null || !EffectivePermissions(member).CanReorderItems) return ServiceResult.Fail("Forbidden", 403);

        var items = await _context.WatchlistItems
            .Where(i => i.WatchlistId == watchlistId)
            .ToListAsync();

        if (items.Count != dto.ItemIds.Count || items.Select(i => i.Id).Except(dto.ItemIds).Any())
            return ServiceResult.Fail("Reorder payload must contain every item exactly once", 400);

        var positionMap = dto.ItemIds.Select((itemId, index) => new { itemId, index })
            .ToDictionary(x => x.itemId, x => x.index);

        foreach (var item in items)
            item.Position = positionMap[item.Id];

        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<WatchlistInvitationDto>> InviteMemberAsync(int currentUserId, int watchlistId, InviteWatchlistMemberDto dto)
    {
        var member = await GetMemberAsync(watchlistId, currentUserId);
        if (member is null || !EffectivePermissions(member).CanInviteMembers) return ServiceResult<WatchlistInvitationDto>.Fail("Forbidden", 403);

        var invitedUser = await ResolveUserAsync(dto.UserId, dto.Username);
        if (invitedUser is null) return ServiceResult<WatchlistInvitationDto>.Fail("User not found", 404);
        if (invitedUser.Id == currentUserId) return ServiceResult<WatchlistInvitationDto>.Fail("You already have access to this watchlist", 400);

        if (await _context.WatchlistMembers.AnyAsync(m => m.WatchlistId == watchlistId && m.UserId == invitedUser.Id))
            return ServiceResult<WatchlistInvitationDto>.Fail("User is already a member", 409);

        var pending = await _context.WatchlistInvitations
            .FirstOrDefaultAsync(i => i.WatchlistId == watchlistId
                && i.InvitedUserId == invitedUser.Id
                && i.Status == WatchlistInvitationStatus.Pending);
        if (pending is not null) return ServiceResult<WatchlistInvitationDto>.Fail("User already has a pending invitation", 409);

        var role = dto.Role == WatchlistRole.Owner ? WatchlistRole.Member : dto.Role;
        var defaults = DefaultsForRole(role);
        ApplyPermissionOverride(defaults, dto.Permissions);

        var invitation = new WatchlistInvitation
        {
            WatchlistId = watchlistId,
            InvitedUserId = invitedUser.Id,
            InvitedByUserId = currentUserId,
            Role = role,
            CanAddItems = defaults.CanAddItems,
            CanRemoveItems = defaults.CanRemoveItems,
            CanReorderItems = defaults.CanReorderItems,
            CanUpdateItemStatus = defaults.CanUpdateItemStatus,
            CanInviteMembers = defaults.CanInviteMembers,
            CanManageMembers = defaults.CanManageMembers,
            CanUpdateWatchlist = defaults.CanUpdateWatchlist,
            Message = dto.Message
        };

        _context.WatchlistInvitations.Add(invitation);
        await _context.SaveChangesAsync();

        var loaded = await _context.WatchlistInvitations
            .Include(i => i.Watchlist)
            .Include(i => i.InvitedByUser)
            .FirstAsync(i => i.Id == invitation.Id);

        return ServiceResult<WatchlistInvitationDto>.Ok(new WatchlistInvitationDto
        {
            Id = loaded.Id,
            WatchlistId = loaded.WatchlistId,
            WatchlistName = loaded.Watchlist.Name,
            WatchlistDescription = loaded.Watchlist.Description,
            InvitedByUserId = loaded.InvitedByUserId,
            InvitedByUsername = loaded.InvitedByUser.Username,
            Role = loaded.Role,
            Status = loaded.Status,
            CreatedAt = loaded.CreatedAt
        });
    }

    public async Task<ServiceResult> AcceptInvitationAsync(int currentUserId, int invitationId)
    {
        var invitation = await _context.WatchlistInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.InvitedUserId == currentUserId);
        if (invitation is null) return ServiceResult.Fail("Invitation not found", 404);
        if (invitation.Status != WatchlistInvitationStatus.Pending)
            return ServiceResult.Fail("Invitation is no longer pending", 409);

        if (!await _context.WatchlistMembers.AnyAsync(m => m.WatchlistId == invitation.WatchlistId && m.UserId == currentUserId))
        {
            _context.WatchlistMembers.Add(new WatchlistMember
            {
                WatchlistId = invitation.WatchlistId,
                UserId = currentUserId,
                Role = invitation.Role,
                InvitedByUserId = invitation.InvitedByUserId,
                CanAddItems = invitation.CanAddItems,
                CanRemoveItems = invitation.CanRemoveItems,
                CanReorderItems = invitation.CanReorderItems,
                CanUpdateItemStatus = invitation.CanUpdateItemStatus,
                CanInviteMembers = invitation.CanInviteMembers,
                CanManageMembers = invitation.CanManageMembers,
                CanUpdateWatchlist = invitation.CanUpdateWatchlist
            });
        }

        invitation.Status = WatchlistInvitationStatus.Accepted;
        invitation.RespondedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RejectInvitationAsync(int currentUserId, int invitationId)
    {
        var invitation = await _context.WatchlistInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.InvitedUserId == currentUserId);
        if (invitation is null) return ServiceResult.Fail("Invitation not found", 404);

        invitation.Status = WatchlistInvitationStatus.Rejected;
        invitation.RespondedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> UpdateMemberAsync(int currentUserId, int watchlistId, int memberId, UpdateWatchlistMemberDto dto)
    {
        var actor = await GetMemberAsync(watchlistId, currentUserId);
        if (actor is null || !EffectivePermissions(actor).CanManageMembers) return ServiceResult.Fail("Forbidden", 403);

        var target = await _context.WatchlistMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.WatchlistId == watchlistId);
        if (target is null) return ServiceResult.Fail("Member not found", 404);
        if (target.Role == WatchlistRole.Owner) return ServiceResult.Fail("Forbidden", 403);
        if (dto.Role == WatchlistRole.Owner) return ServiceResult.Fail("Owner role cannot be assigned", 400);
        if (target.Role == WatchlistRole.Admin && actor.Role != WatchlistRole.Owner) return ServiceResult.Fail("Forbidden", 403);

        target.Role = dto.Role;
        target.CanAddItems = dto.Permissions.CanAddItems;
        target.CanRemoveItems = dto.Permissions.CanRemoveItems;
        target.CanReorderItems = dto.Permissions.CanReorderItems;
        target.CanUpdateItemStatus = dto.Permissions.CanUpdateItemStatus;
        target.CanInviteMembers = dto.Permissions.CanInviteMembers;
        target.CanManageMembers = dto.Permissions.CanManageMembers;
        target.CanUpdateWatchlist = dto.Permissions.CanUpdateWatchlist;
        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RemoveMemberAsync(int currentUserId, int watchlistId, int memberId)
    {
        var actor = await GetMemberAsync(watchlistId, currentUserId);
        if (actor is null || !EffectivePermissions(actor).CanManageMembers) return ServiceResult.Fail("Forbidden", 403);

        var target = await _context.WatchlistMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.WatchlistId == watchlistId);
        if (target is null) return ServiceResult.Fail("Member not found", 404);
        if (target.Role == WatchlistRole.Owner) return ServiceResult.Fail("Forbidden", 403);
        if (target.Role == WatchlistRole.Admin && actor.Role != WatchlistRole.Owner) return ServiceResult.Fail("Forbidden", 403);

        _context.WatchlistMembers.Remove(target);
        var preference = await _context.UserWatchlistPreferences
            .FirstOrDefaultAsync(p => p.UserId == target.UserId && p.DefaultWatchlistId == watchlistId);
        if (preference is not null)
            preference.DefaultWatchlistId = null;
        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> RequestAccessAsync(int currentUserId, int watchlistId, CreateWatchlistAccessRequestDto dto)
    {
        if (await _context.WatchlistMembers.AnyAsync(m => m.WatchlistId == watchlistId && m.UserId == currentUserId))
            return ServiceResult.Fail("You already have access", 409);

        if (!await _context.Watchlists.AnyAsync(w => w.Id == watchlistId))
            return ServiceResult.Fail("Watchlist not found", 404);

        var pending = await _context.WatchlistAccessRequests
            .AnyAsync(r => r.WatchlistId == watchlistId
                && r.RequestingUserId == currentUserId
                && r.Status == WatchlistAccessRequestStatus.Pending);
        if (pending) return ServiceResult.Fail("Access request already pending", 409);

        _context.WatchlistAccessRequests.Add(new WatchlistAccessRequest
        {
            WatchlistId = watchlistId,
            RequestingUserId = currentUserId,
            Message = dto.Message
        });
        await _context.SaveChangesAsync();

        return ServiceResult.Ok();
    }

    public async Task<ServiceResult> ApproveAccessRequestAsync(int currentUserId, int requestId)
    {
        return await ResolveAccessRequestAsync(currentUserId, requestId, true);
    }

    public async Task<ServiceResult> RejectAccessRequestAsync(int currentUserId, int requestId)
    {
        return await ResolveAccessRequestAsync(currentUserId, requestId, false);
    }

    public async Task<ServiceResult> SetDefaultWatchlistAsync(int currentUserId, SetDefaultWatchlistDto dto)
    {
        if (dto.WatchlistId.HasValue && !await _context.WatchlistMembers.AnyAsync(m => m.UserId == currentUserId && m.WatchlistId == dto.WatchlistId.Value))
            return ServiceResult.Fail("Forbidden", 403);

        var preference = await _context.UserWatchlistPreferences.FirstOrDefaultAsync(p => p.UserId == currentUserId);
        if (preference is null)
        {
            preference = new UserWatchlistPreference { UserId = currentUserId };
            _context.UserWatchlistPreferences.Add(preference);
        }

        preference.DefaultWatchlistId = dto.WatchlistId;
        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    // --- Private helper methods (moved from controller) ---

    private async Task<ServiceResult?> AddItemInternalAsync(int watchlistId, AddWatchlistItemDto dto, int userId)
    {
        if (!await _context.Watchlists.AnyAsync(w => w.Id == watchlistId))
            return ServiceResult.Fail("Watchlist not found", 404);

        if (dto.ItemType == WatchlistItemType.MediaItem)
        {
            if (!dto.MediaItemId.HasValue)
                return ServiceResult.Fail("MediaItemId is required", 400);

            if (!await _context.MediaItems.AnyAsync(m => m.Id == dto.MediaItemId.Value))
                return ServiceResult.Fail("Media item not found", 404);

            if (await _context.WatchlistItems.AnyAsync(i => i.WatchlistId == watchlistId && i.MediaItemId == dto.MediaItemId.Value))
                return ServiceResult.Fail("Media item is already in this watchlist", 409);
        }
        else
        {
            if (!dto.ChildWatchlistId.HasValue)
                return ServiceResult.Fail("ChildWatchlistId is required", 400);

            if (dto.ChildWatchlistId.Value == watchlistId)
                return ServiceResult.Fail("A watchlist cannot contain itself", 400);

            if (!await _context.Watchlists.AnyAsync(w => w.Id == dto.ChildWatchlistId.Value))
                return ServiceResult.Fail("Child watchlist not found", 404);

            if (await _context.WatchlistItems.AnyAsync(i => i.WatchlistId == watchlistId && i.ChildWatchlistId == dto.ChildWatchlistId.Value))
                return ServiceResult.Fail("Watchlist is already nested here", 409);

            if (await WouldCreateCycleAsync(watchlistId, dto.ChildWatchlistId.Value))
                return ServiceResult.Fail("Nested watchlist would create a cycle", 400);
        }

        var position = dto.Position ?? await _context.WatchlistItems
            .Where(i => i.WatchlistId == watchlistId)
            .Select(i => (int?)i.Position)
            .MaxAsync() + 1 ?? 0;

        if (dto.Position.HasValue)
        {
            position = Math.Max(0, dto.Position.Value);
            var affected = await _context.WatchlistItems
                .Where(i => i.WatchlistId == watchlistId && i.Position >= position)
                .ToListAsync();
            foreach (var item in affected)
                item.Position++;
        }

        _context.WatchlistItems.Add(new WatchlistItem
        {
            WatchlistId = watchlistId,
            ItemType = dto.ItemType,
            MediaItemId = dto.ItemType == WatchlistItemType.MediaItem ? dto.MediaItemId : null,
            ChildWatchlistId = dto.ItemType == WatchlistItemType.Watchlist ? dto.ChildWatchlistId : null,
            Status = dto.Status,
            Position = position,
            AddedByUserId = userId
        });

        await _context.SaveChangesAsync();
        await NormalizePositionsAsync(watchlistId);
        return null;
    }

    private async Task<ServiceResult> ResolveAccessRequestAsync(int currentUserId, int requestId, bool approve)
    {
        var request = await _context.WatchlistAccessRequests
            .FirstOrDefaultAsync(r => r.Id == requestId);
        if (request is null) return ServiceResult.Fail("Access request not found", 404);

        var actor = await GetMemberAsync(request.WatchlistId, currentUserId);
        if (actor is null || !EffectivePermissions(actor).CanManageMembers) return ServiceResult.Fail("Forbidden", 403);

        request.Status = approve ? WatchlistAccessRequestStatus.Approved : WatchlistAccessRequestStatus.Rejected;
        request.RespondedAt = DateTime.UtcNow;
        request.RespondedByUserId = currentUserId;

        if (approve && !await _context.WatchlistMembers.AnyAsync(m => m.WatchlistId == request.WatchlistId && m.UserId == request.RequestingUserId))
        {
            var defaults = DefaultsForRole(WatchlistRole.Member);
            _context.WatchlistMembers.Add(new WatchlistMember
            {
                WatchlistId = request.WatchlistId,
                UserId = request.RequestingUserId,
                InvitedByUserId = currentUserId,
                Role = WatchlistRole.Member,
                CanAddItems = defaults.CanAddItems,
                CanRemoveItems = defaults.CanRemoveItems,
                CanReorderItems = defaults.CanReorderItems,
                CanUpdateItemStatus = defaults.CanUpdateItemStatus,
                CanInviteMembers = defaults.CanInviteMembers,
                CanManageMembers = defaults.CanManageMembers,
                CanUpdateWatchlist = defaults.CanUpdateWatchlist
            });
        }

        await _context.SaveChangesAsync();
        return ServiceResult.Ok();
    }

    private async Task<Watchlist?> LoadWatchlistAsync(int id)
    {
        return await _context.Watchlists
            .Include(w => w.OwnerUser)
            .Include(w => w.Members)
                .ThenInclude(m => m.User)
            .Include(w => w.Items.OrderBy(i => i.Position))
                .ThenInclude(i => i.MediaItem)
                    .ThenInclude(mi => mi!.Series)
            .Include(w => w.Items.OrderBy(i => i.Position))
                .ThenInclude(i => i.MediaItem)
                    .ThenInclude(mi => mi!.Movie)
            .Include(w => w.Items.OrderBy(i => i.Position))
                .ThenInclude(i => i.ChildWatchlist)
            .Include(w => w.Items.OrderBy(i => i.Position))
                .ThenInclude(i => i.AddedByUser)
            .FirstOrDefaultAsync(w => w.Id == id);
    }

    private async Task<WatchlistMember?> GetMemberAsync(int watchlistId, int userId)
    {
        return await _context.WatchlistMembers
            .Include(m => m.User)
            .FirstOrDefaultAsync(m => m.WatchlistId == watchlistId && m.UserId == userId);
    }

    private WatchlistSummaryDto MapSummary(Watchlist watchlist, WatchlistMember member)
    {
        return new WatchlistSummaryDto
        {
            Id = watchlist.Id,
            Name = watchlist.Name,
            Description = watchlist.Description,
            CoverUrl = $"/api/watchlists/{watchlist.Id}/cover",
            State = watchlist.State,
            OwnerUserId = watchlist.OwnerUserId,
            OwnerUsername = watchlist.OwnerUser.Username,
            Role = member.Role,
            Permissions = EffectivePermissions(member),
            ItemCount = watchlist.Items.Count,
            CreatedAt = watchlist.CreatedAt,
            UpdatedAt = watchlist.UpdatedAt
        };
    }

    private async Task<WatchlistDetailDto> MapDetailAsync(Watchlist watchlist, WatchlistMember? member, int userId, int? profileId)
    {
        var permissions = member is not null ? EffectivePermissions(member) : new WatchlistPermissionsDto();
        var role = member?.Role ?? WatchlistRole.Member;

        return new WatchlistDetailDto
        {
            Id = watchlist.Id,
            Name = watchlist.Name,
            Description = watchlist.Description,
            CoverUrl = $"/api/watchlists/{watchlist.Id}/cover",
            State = watchlist.State,
            OwnerUserId = watchlist.OwnerUserId,
            OwnerUsername = watchlist.OwnerUser.Username,
            Role = role,
            Permissions = permissions,
            ItemCount = watchlist.Items.Count,
            CreatedAt = watchlist.CreatedAt,
            UpdatedAt = watchlist.UpdatedAt,
            Members = watchlist.Members.OrderBy(m => m.Role).ThenBy(m => m.User.Username).Select(MapMember).ToList(),
            Items = await MapItemsAsync(watchlist.Items.OrderBy(i => i.Position).ToList(), userId, profileId, 0, new HashSet<int> { watchlist.Id })
        };
    }

    private WatchlistMemberDto MapMember(WatchlistMember member)
    {
        return new WatchlistMemberDto
        {
            Id = member.Id,
            UserId = member.UserId,
            Username = member.User.Username,
            Role = member.Role,
            Permissions = EffectivePermissions(member),
            CreatedAt = member.CreatedAt
        };
    }

    private async Task<List<WatchlistItemDto>> MapItemsAsync(List<WatchlistItem> items, int userId, int? profileId, int depth, HashSet<int> path)
    {
        var result = new List<WatchlistItemDto>();
        foreach (var item in items.OrderBy(i => i.Position))
            result.Add(await MapItemAsync(item, userId, profileId, depth, path));
        return result;
    }

    private async Task<WatchlistItemDto> MapItemAsync(WatchlistItem item, int userId, int? profileId, int depth, HashSet<int> path)
    {
        return new WatchlistItemDto
        {
            Id = item.Id,
            ItemType = item.ItemType,
            MediaItemId = item.MediaItemId,
            ChildWatchlistId = item.ChildWatchlistId,
            Status = item.Status,
            Position = item.Position,
            AddedByUserId = item.AddedByUserId,
            AddedByUsername = item.AddedByUser?.Username,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            Media = item.MediaItem is not null ? await MapMediaAsync(item.MediaItem, profileId) : null,
            ChildWatchlist = item.ChildWatchlistId.HasValue && depth < MaxNestedDepth && !path.Contains(item.ChildWatchlistId.Value)
                ? await MapChildWatchlistAsync(item.ChildWatchlistId.Value, userId, profileId, depth + 1, path)
                : null
        };
    }

    private async Task<WatchlistChildDto?> MapChildWatchlistAsync(int watchlistId, int userId, int? profileId, int depth, bool hasParentAccess)
    {
        var watchlist = await LoadWatchlistAsync(watchlistId);
        if (watchlist is null) return null;

        var hasFullAccess = await _context.WatchlistMembers.AnyAsync(m => m.WatchlistId == watchlistId && m.UserId == userId);
        var hasPendingRequest = await _context.WatchlistAccessRequests.AnyAsync(r =>
            r.WatchlistId == watchlistId
            && r.RequestingUserId == userId
            && r.Status == WatchlistAccessRequestStatus.Pending);

        var path = new HashSet<int> { watchlistId };
        var items = depth >= MaxNestedDepth
            ? new List<WatchlistItemDto>()
            : await MapItemsAsync(watchlist.Items.OrderBy(i => i.Position).ToList(), userId, profileId, depth, path);

        var hasCover = watchlist.CoverImagePath != null
            || watchlist.Items.Any(i => i.MediaItem != null);

        return new WatchlistChildDto
        {
            Id = watchlist.Id,
            Name = watchlist.Name,
            Description = watchlist.Description,
            CoverUrl = hasCover ? $"/api/watchlists/{watchlist.Id}/cover" : null,
            State = watchlist.State,
            HasFullAccess = hasFullAccess,
            CanRequestAccess = hasParentAccess && !hasFullAccess && !hasPendingRequest,
            Items = items
        };
    }

    private async Task<WatchlistChildDto?> MapChildWatchlistAsync(int watchlistId, int userId, int? profileId, int depth, HashSet<int> path)
    {
        var watchlist = await LoadWatchlistAsync(watchlistId);
        if (watchlist is null) return null;

        var hasFullAccess = await _context.WatchlistMembers.AnyAsync(m => m.WatchlistId == watchlistId && m.UserId == userId);
        var hasPendingRequest = await _context.WatchlistAccessRequests.AnyAsync(r =>
            r.WatchlistId == watchlistId
            && r.RequestingUserId == userId
            && r.Status == WatchlistAccessRequestStatus.Pending);

        path.Add(watchlistId);
        var items = await MapItemsAsync(watchlist.Items.OrderBy(i => i.Position).ToList(), userId, profileId, depth, path);
        path.Remove(watchlistId);

        var hasCover = watchlist.CoverImagePath != null
            || watchlist.Items.Any(i => i.MediaItem != null);

        return new WatchlistChildDto
        {
            Id = watchlist.Id,
            Name = watchlist.Name,
            Description = watchlist.Description,
            CoverUrl = hasCover ? $"/api/watchlists/{watchlist.Id}/cover" : null,
            State = watchlist.State,
            HasFullAccess = hasFullAccess,
            CanRequestAccess = !hasFullAccess && !hasPendingRequest,
            Items = items
        };
    }

    private async Task<WatchlistMediaItemDto> MapMediaAsync(MediaItem mediaItem, int? profileId)
    {
        var isInProfile = false;
        var isBlacklisted = false;
        decimal? userRating = null;

        if (profileId.HasValue)
        {
            isInProfile = await _context.ProfileWatchStates
                .AnyAsync(ws => ws.ProfileId == profileId.Value && ws.MediaItemId == mediaItem.Id);
            isBlacklisted = await _context.ProfileMediaBlocks
                .AnyAsync(b => b.ProfileId == profileId.Value && b.MediaItemId == mediaItem.Id);
            userRating = await _context.ProfileWatchStates
                .Where(ws => ws.ProfileId == profileId.Value && ws.MediaItemId == mediaItem.Id && ws.EpisodeId == null && ws.MovieId == null)
                .Select(ws => ws.UserRating)
                .FirstOrDefaultAsync();
        }

        return new WatchlistMediaItemDto
        {
            MediaItemId = mediaItem.Id,
            MediaType = mediaItem.MediaType,
            SeriesId = mediaItem.Series?.Id,
            MovieId = mediaItem.Movie?.Id,
            Title = mediaItem.Title,
            OriginalTitle = mediaItem.OriginalTitle,
            PosterPath = mediaItem.PosterPath,
            ReleaseDate = mediaItem.ReleaseDate,
            UserRating = userRating,
            IsInProfile = isInProfile,
            IsBlacklisted = isBlacklisted,
            CanAddToProfile = profileId.HasValue && (!isInProfile || isBlacklisted)
        };
    }

    private WatchlistPermissionsDto EffectivePermissions(WatchlistMember member)
    {
        if (member.Role == WatchlistRole.Owner)
        {
            return new WatchlistPermissionsDto
            {
                CanAddItems = true,
                CanRemoveItems = true,
                CanReorderItems = true,
                CanUpdateItemStatus = true,
                CanInviteMembers = true,
                CanManageMembers = true,
                CanUpdateWatchlist = true,
                CanDeleteWatchlist = true
            };
        }

        if (member.Role == WatchlistRole.Admin)
        {
            return new WatchlistPermissionsDto
            {
                CanAddItems = true,
                CanRemoveItems = true,
                CanReorderItems = true,
                CanUpdateItemStatus = true,
                CanInviteMembers = true,
                CanManageMembers = true,
                CanUpdateWatchlist = true,
                CanDeleteWatchlist = false
            };
        }

        return new WatchlistPermissionsDto
        {
            CanAddItems = member.CanAddItems,
            CanRemoveItems = member.CanRemoveItems,
            CanReorderItems = member.CanReorderItems,
            CanUpdateItemStatus = member.CanUpdateItemStatus,
            CanInviteMembers = member.CanInviteMembers,
            CanManageMembers = member.CanManageMembers,
            CanUpdateWatchlist = member.CanUpdateWatchlist,
            CanDeleteWatchlist = false
        };
    }

    private WatchlistPermissionsDto DefaultsForRole(WatchlistRole role)
    {
        return role switch
        {
            WatchlistRole.Admin => new WatchlistPermissionsDto
            {
                CanAddItems = true,
                CanRemoveItems = true,
                CanReorderItems = true,
                CanUpdateItemStatus = true,
                CanInviteMembers = true,
                CanManageMembers = true,
                CanUpdateWatchlist = true
            },
            WatchlistRole.Owner => new WatchlistPermissionsDto
            {
                CanAddItems = true,
                CanRemoveItems = true,
                CanReorderItems = true,
                CanUpdateItemStatus = true,
                CanInviteMembers = true,
                CanManageMembers = true,
                CanUpdateWatchlist = true,
                CanDeleteWatchlist = true
            },
            _ => new WatchlistPermissionsDto
            {
                CanAddItems = true,
                CanRemoveItems = false,
                CanReorderItems = true,
                CanUpdateItemStatus = true,
                CanInviteMembers = false,
                CanManageMembers = false,
                CanUpdateWatchlist = false
            }
        };
    }

    private void ApplyPermissionOverride(WatchlistPermissionsDto target, WatchlistPermissionsDto? source)
    {
        if (source is null) return;
        target.CanAddItems = source.CanAddItems;
        target.CanRemoveItems = source.CanRemoveItems;
        target.CanReorderItems = source.CanReorderItems;
        target.CanUpdateItemStatus = source.CanUpdateItemStatus;
        target.CanInviteMembers = source.CanInviteMembers;
        target.CanManageMembers = source.CanManageMembers;
        target.CanUpdateWatchlist = source.CanUpdateWatchlist;
    }

    private async Task<User?> ResolveUserAsync(int? userId, string? username)
    {
        if (userId.HasValue)
            return await _context.Users.FindAsync(userId.Value);

        if (!string.IsNullOrWhiteSpace(username))
        {
            var normalized = username.Trim().ToLower();
            return await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == normalized);
        }

        return null;
    }

    private async Task MoveItemAsync(int watchlistId, WatchlistItem item, int requestedPosition)
    {
        var items = await _context.WatchlistItems
            .Where(i => i.WatchlistId == watchlistId)
            .OrderBy(i => i.Position)
            .ToListAsync();

        items.RemoveAll(i => i.Id == item.Id);
        var position = Math.Clamp(requestedPosition, 0, items.Count);
        items.Insert(position, item);

        for (var i = 0; i < items.Count; i++)
            items[i].Position = i;
    }

    private async Task NormalizePositionsAsync(int watchlistId)
    {
        var items = await _context.WatchlistItems
            .Where(i => i.WatchlistId == watchlistId)
            .OrderBy(i => i.Position)
            .ThenBy(i => i.Id)
            .ToListAsync();

        for (var i = 0; i < items.Count; i++)
            items[i].Position = i;

        await _context.SaveChangesAsync();
    }

    private async Task<bool> WouldCreateCycleAsync(int parentWatchlistId, int childWatchlistId)
    {
        var edges = await _context.WatchlistItems
            .Where(i => i.ItemType == WatchlistItemType.Watchlist && i.ChildWatchlistId.HasValue)
            .Select(i => new { Parent = i.WatchlistId, Child = i.ChildWatchlistId!.Value })
            .ToListAsync();

        var lookup = edges
            .GroupBy(e => e.Parent)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Child).ToList());

        var stack = new Stack<int>();
        var visited = new HashSet<int>();
        stack.Push(childWatchlistId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current)) continue;
            if (current == parentWatchlistId) return true;

            if (lookup.TryGetValue(current, out var children))
            {
                foreach (var child in children)
                    stack.Push(child);
            }
        }

        return false;
    }

    public async Task<ServiceResult<WatchlistExportDto>> ExportWatchlistAsync(int currentUserId, int watchlistId)
    {
        var member = await _context.WatchlistMembers
            .Include(m => m.Watchlist)
                .ThenInclude(w => w.Items)
                    .ThenInclude(i => i.MediaItem)
            .FirstOrDefaultAsync(m => m.WatchlistId == watchlistId && m.UserId == currentUserId);

        if (member is null)
            return ServiceResult<WatchlistExportDto>.Fail("Watchlist not found or access denied", 404);

        var watchlist = member.Watchlist;
        var exportItems = watchlist.Items
            .Where(i => i.ItemType == WatchlistItemType.MediaItem && i.MediaItem != null)
            .OrderBy(i => i.Position)
            .Select(i => new WatchlistExportItemDto
            {
                MediaType = i.MediaItem!.MediaType.ToString(),
                Title = i.MediaItem.Title,
                TmdbId = i.MediaItem.TmdbId,
                ImdbId = i.MediaItem.ImdbId,
                Status = i.Status.ToString(),
                Position = i.Position,
            })
            .ToList();

        return ServiceResult<WatchlistExportDto>.Ok(new WatchlistExportDto
        {
            Name = watchlist.Name,
            Description = watchlist.Description,
            State = watchlist.State.ToString(),
            ExportedAt = DateTime.UtcNow,
            Items = exportItems,
        });
    }

    public async Task<ServiceResult<WatchlistImportResultDto>> ImportWatchlistAsync(int currentUserId, WatchlistImportDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return ServiceResult<WatchlistImportResultDto>.Fail("Watchlist name is required", 400);

        if (dto.Items.Count == 0)
            return ServiceResult<WatchlistImportResultDto>.Fail("No items to import", 400);

        var state = WatchlistState.Pending;
        if (!string.IsNullOrEmpty(dto.State) && Enum.TryParse<WatchlistState>(dto.State, true, out var parsedState))
            state = parsedState;

        var watchlist = new Watchlist
        {
            Name = dto.Name,
            Description = dto.Description,
            OwnerUserId = currentUserId,
            State = state,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _context.Watchlists.Add(watchlist);
        await _context.SaveChangesAsync();

        _context.WatchlistMembers.Add(new WatchlistMember
        {
            WatchlistId = watchlist.Id,
            UserId = currentUserId,
            Role = WatchlistRole.Owner,
            CanAddItems = true,
            CanRemoveItems = true,
            CanReorderItems = true,
            CanUpdateItemStatus = true,
            CanInviteMembers = true,
            CanManageMembers = true,
            CanUpdateWatchlist = true,
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        var imported = 0;
        var skipped = 0;
        var errors = new List<string>();

        for (var i = 0; i < dto.Items.Count; i++)
        {
            var item = dto.Items[i];

            if (!item.TmdbId.HasValue)
            {
                errors.Add($"Item {i + 1} '{item.Title}': No TMDB ID — skipped");
                skipped++;
                continue;
            }

            if (!Enum.TryParse<MediaType>(item.MediaType, true, out var mediaType))
            {
                errors.Add($"Item {i + 1} '{item.Title}': Invalid media type '{item.MediaType}' — skipped");
                skipped++;
                continue;
            }

            var mediaItem = await _context.MediaItems
                .FirstOrDefaultAsync(m => m.TmdbId == item.TmdbId && m.MediaType == mediaType);

            if (mediaItem is null)
            {
                try
                {
                    var syntheticId = $"watchlist-import-{item.TmdbId}";
                    if (mediaType == MediaType.Series)
                    {
                        mediaItem = await _metadata.ResolveSeriesAsync(syntheticId, item.Title, tmdbId: item.TmdbId);
                        if (mediaItem != null)
                        {
                            var series = await _context.Series.FirstOrDefaultAsync(s => s.MediaItemId == mediaItem.Id);
                            if (series != null)
                                await _metadata.PopulateSeasonsAndEpisodesAsync(series.Id);
                            await _metadata.RefreshTranslationsAsync(mediaItem.Id);
                            await _metadata.RefreshImagesAsync(mediaItem.Id);
                        }
                    }
                    else
                    {
                        mediaItem = await _metadata.ResolveMovieAsync(syntheticId, item.Title, tmdbId: item.TmdbId);
                        if (mediaItem != null)
                        {
                            await _metadata.RefreshTranslationsAsync(mediaItem.Id);
                            await _metadata.RefreshImagesAsync(mediaItem.Id);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to import TMDB {TmdbId} ({Title})", item.TmdbId, item.Title);
                }

                if (mediaItem is null)
                {
                    errors.Add($"Item {i + 1} '{item.Title}' (TMDB {item.TmdbId}): Could not resolve from TMDB — skipped");
                    skipped++;
                    continue;
                }
            }

            var itemStatus = WatchlistStatus.WantToWatch;
            if (!string.IsNullOrEmpty(item.Status) && Enum.TryParse<WatchlistStatus>(item.Status, true, out var parsedStatus))
                itemStatus = parsedStatus;

            _context.Set<WatchlistItem>().Add(new WatchlistItem
            {
                WatchlistId = watchlist.Id,
                ItemType = WatchlistItemType.MediaItem,
                MediaItemId = mediaItem.Id,
                Status = itemStatus,
                Position = item.Position ?? imported,
                AddedByUserId = currentUserId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
            imported++;
        }

        await _context.SaveChangesAsync();

        return ServiceResult<WatchlistImportResultDto>.Ok(new WatchlistImportResultDto
        {
            WatchlistId = watchlist.Id,
            WatchlistName = watchlist.Name,
            TotalItems = dto.Items.Count,
            ImportedItems = imported,
            SkippedItems = skipped,
            Errors = errors.Take(50).ToList(),
        });
    }
}
