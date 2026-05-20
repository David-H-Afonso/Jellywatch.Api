using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain;
using Jellywatch.Api.Domain.Enums;
using Jellywatch.Api.Infrastructure;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class WatchlistsController : BaseApiController
{
    private const int MaxNestedDepth = 5;
    private readonly JellywatchDbContext _context;

    public WatchlistsController(JellywatchDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<WatchlistIndexDto>> GetWatchlists([FromQuery] int? profileId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var members = await _context.WatchlistMembers
            .Include(m => m.Watchlist)
                .ThenInclude(w => w.OwnerUser)
            .Include(m => m.Watchlist)
                .ThenInclude(w => w.Items)
            .Where(m => m.UserId == userId.Value)
            .OrderBy(m => m.Watchlist.Name)
            .ToListAsync();

        var summaries = members.Select(m => MapSummary(m.Watchlist, m)).ToList();

        var invitations = await _context.WatchlistInvitations
            .Include(i => i.Watchlist)
                .ThenInclude(w => w.OwnerUser)
            .Include(i => i.InvitedByUser)
            .Where(i => i.InvitedUserId == userId.Value && i.Status == WatchlistInvitationStatus.Pending)
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync();

        var incomingRequests = await _context.WatchlistAccessRequests
            .Include(r => r.Watchlist)
            .Include(r => r.RequestingUser)
            .Where(r => r.Status == WatchlistAccessRequestStatus.Pending
                && r.Watchlist.Members.Any(m => m.UserId == userId.Value
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
            .FirstOrDefaultAsync(p => p.UserId == userId.Value);

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
                Preview = await MapChildWatchlistAsync(invitation.WatchlistId, userId.Value, profileId, 0, false)
            });
        }

        return Ok(new WatchlistIndexDto
        {
            Watchlists = summaries,
            PendingInvitations = invitationDtos,
            IncomingAccessRequests = incomingRequests,
            DefaultWatchlistId = preference?.DefaultWatchlistId
        });
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<WatchlistUserOptionDto>>> GetUserOptions([FromQuery] string? search, [FromQuery] int? watchlistId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var normalizedSearch = search?.Trim().ToLower();
        var query = _context.Users
            .Where(u => u.Id != userId.Value);

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

        return Ok(users);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WatchlistDetailDto>> GetWatchlist(int id, [FromQuery] int? profileId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var member = await GetMemberAsync(id, userId.Value);
        if (member is null) return Forbid();

        var watchlist = await LoadWatchlistAsync(id);
        if (watchlist is null) return NotFound(new { message = "Watchlist not found" });

        return Ok(await MapDetailAsync(watchlist, member, userId.Value, profileId));
    }

    [HttpPost]
    public async Task<ActionResult<WatchlistDetailDto>> CreateWatchlist([FromBody] CreateWatchlistDto dto, [FromQuery] int? profileId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var name = dto.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Name is required" });

        var watchlist = new Watchlist
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            OwnerUserId = userId.Value,
            State = dto.State
        };

        _context.Watchlists.Add(watchlist);
        await _context.SaveChangesAsync();

        _context.WatchlistMembers.Add(new WatchlistMember
        {
            WatchlistId = watchlist.Id,
            UserId = userId.Value,
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
            var result = await AddItemInternalAsync(watchlist.Id, dto.InitialItem, userId.Value);
            if (result is not null) return result;
        }

        var created = await LoadWatchlistAsync(watchlist.Id);
        var member = await GetMemberAsync(watchlist.Id, userId.Value);
        return CreatedAtAction(nameof(GetWatchlist), new { id = watchlist.Id }, await MapDetailAsync(created!, member, userId.Value, profileId));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateWatchlist(int id, [FromBody] UpdateWatchlistDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var member = await GetMemberAsync(id, userId.Value);
        if (member is null || !EffectivePermissions(member).CanUpdateWatchlist) return Forbid();

        var watchlist = await _context.Watchlists.FindAsync(id);
        if (watchlist is null) return NotFound(new { message = "Watchlist not found" });

        var name = dto.Name.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "Name is required" });

        watchlist.Name = name;
        watchlist.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        watchlist.State = dto.State;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteWatchlist(int id)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var member = await GetMemberAsync(id, userId.Value);
        if (member is null || member.Role != WatchlistRole.Owner) return Forbid();

        var watchlist = await _context.Watchlists.FindAsync(id);
        if (watchlist is null) return NotFound(new { message = "Watchlist not found" });

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
        return NoContent();
    }

    [HttpDelete("{id:int}/me")]
    public async Task<IActionResult> LeaveWatchlist(int id)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var member = await _context.WatchlistMembers
            .FirstOrDefaultAsync(m => m.WatchlistId == id && m.UserId == userId.Value);
        if (member is null) return NotFound(new { message = "Watchlist membership not found" });
        if (member.Role == WatchlistRole.Owner)
            return BadRequest(new { message = "Owner cannot leave their own watchlist. Delete it or transfer ownership first." });

        _context.WatchlistMembers.Remove(member);

        var preference = await _context.UserWatchlistPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId.Value && p.DefaultWatchlistId == id);
        if (preference is not null)
            preference.DefaultWatchlistId = null;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> CompleteWatchlist(int id)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var member = await GetMemberAsync(id, userId.Value);
        if (member is null || !EffectivePermissions(member).CanUpdateWatchlist) return Forbid();

        var watchlist = await _context.Watchlists.FindAsync(id);
        if (watchlist is null) return NotFound(new { message = "Watchlist not found" });

        watchlist.State = WatchlistState.Completed;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/items")]
    public async Task<IActionResult> AddItem(int id, [FromBody] AddWatchlistItemDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var member = await GetMemberAsync(id, userId.Value);
        if (member is null || !EffectivePermissions(member).CanAddItems) return Forbid();

        var result = await AddItemInternalAsync(id, dto, userId.Value);
        if (result is not null) return result;

        return NoContent();
    }

    [HttpPut("{id:int}/items/{itemId:int}")]
    public async Task<IActionResult> UpdateItem(int id, int itemId, [FromBody] UpdateWatchlistItemDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var member = await GetMemberAsync(id, userId.Value);
        if (member is null) return Forbid();

        var permissions = EffectivePermissions(member);
        if (!permissions.CanUpdateItemStatus && !permissions.CanReorderItems) return Forbid();

        var item = await _context.WatchlistItems.FirstOrDefaultAsync(i => i.Id == itemId && i.WatchlistId == id);
        if (item is null) return NotFound(new { message = "Watchlist item not found" });

        if (permissions.CanUpdateItemStatus)
            item.Status = dto.Status;

        if (dto.Position.HasValue)
        {
            if (!permissions.CanReorderItems) return Forbid();
            await MoveItemAsync(id, item, dto.Position.Value);
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:int}/items/{itemId:int}")]
    public async Task<IActionResult> DeleteItem(int id, int itemId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var member = await GetMemberAsync(id, userId.Value);
        if (member is null || !EffectivePermissions(member).CanRemoveItems) return Forbid();

        var item = await _context.WatchlistItems.FirstOrDefaultAsync(i => i.Id == itemId && i.WatchlistId == id);
        if (item is null) return NotFound(new { message = "Watchlist item not found" });

        _context.WatchlistItems.Remove(item);
        await _context.SaveChangesAsync();
        await NormalizePositionsAsync(id);

        return NoContent();
    }

    [HttpPut("{id:int}/items/reorder")]
    public async Task<IActionResult> ReorderItems(int id, [FromBody] ReorderWatchlistItemsDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var member = await GetMemberAsync(id, userId.Value);
        if (member is null || !EffectivePermissions(member).CanReorderItems) return Forbid();

        var items = await _context.WatchlistItems
            .Where(i => i.WatchlistId == id)
            .ToListAsync();

        if (items.Count != dto.ItemIds.Count || items.Select(i => i.Id).Except(dto.ItemIds).Any())
            return BadRequest(new { message = "Reorder payload must contain every item exactly once" });

        var positionMap = dto.ItemIds.Select((itemId, index) => new { itemId, index })
            .ToDictionary(x => x.itemId, x => x.index);

        foreach (var item in items)
            item.Position = positionMap[item.Id];

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/members/invite")]
    public async Task<ActionResult<WatchlistInvitationDto>> InviteMember(int id, [FromBody] InviteWatchlistMemberDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var member = await GetMemberAsync(id, userId.Value);
        if (member is null || !EffectivePermissions(member).CanInviteMembers) return Forbid();

        var invitedUser = await ResolveUserAsync(dto.UserId, dto.Username);
        if (invitedUser is null) return NotFound(new { message = "User not found" });
        if (invitedUser.Id == userId.Value) return BadRequest(new { message = "You already have access to this watchlist" });

        if (await _context.WatchlistMembers.AnyAsync(m => m.WatchlistId == id && m.UserId == invitedUser.Id))
            return Conflict(new { message = "User is already a member" });

        var pending = await _context.WatchlistInvitations
            .FirstOrDefaultAsync(i => i.WatchlistId == id
                && i.InvitedUserId == invitedUser.Id
                && i.Status == WatchlistInvitationStatus.Pending);
        if (pending is not null) return Conflict(new { message = "User already has a pending invitation" });

        var role = dto.Role == WatchlistRole.Owner ? WatchlistRole.Member : dto.Role;
        var defaults = DefaultsForRole(role);
        ApplyPermissionOverride(defaults, dto.Permissions);

        var invitation = new WatchlistInvitation
        {
            WatchlistId = id,
            InvitedUserId = invitedUser.Id,
            InvitedByUserId = userId.Value,
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

        return Ok(new WatchlistInvitationDto
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

    [HttpPost("invitations/{invitationId:int}/accept")]
    public async Task<IActionResult> AcceptInvitation(int invitationId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var invitation = await _context.WatchlistInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.InvitedUserId == userId.Value);
        if (invitation is null) return NotFound(new { message = "Invitation not found" });
        if (invitation.Status != WatchlistInvitationStatus.Pending)
            return Conflict(new { message = "Invitation is no longer pending" });

        if (!await _context.WatchlistMembers.AnyAsync(m => m.WatchlistId == invitation.WatchlistId && m.UserId == userId.Value))
        {
            _context.WatchlistMembers.Add(new WatchlistMember
            {
                WatchlistId = invitation.WatchlistId,
                UserId = userId.Value,
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

        return NoContent();
    }

    [HttpPost("invitations/{invitationId:int}/reject")]
    public async Task<IActionResult> RejectInvitation(int invitationId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var invitation = await _context.WatchlistInvitations
            .FirstOrDefaultAsync(i => i.Id == invitationId && i.InvitedUserId == userId.Value);
        if (invitation is null) return NotFound(new { message = "Invitation not found" });

        invitation.Status = WatchlistInvitationStatus.Rejected;
        invitation.RespondedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPut("{id:int}/members/{memberId:int}")]
    public async Task<IActionResult> UpdateMember(int id, int memberId, [FromBody] UpdateWatchlistMemberDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var actor = await GetMemberAsync(id, userId.Value);
        if (actor is null || !EffectivePermissions(actor).CanManageMembers) return Forbid();

        var target = await _context.WatchlistMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.WatchlistId == id);
        if (target is null) return NotFound(new { message = "Member not found" });
        if (target.Role == WatchlistRole.Owner) return Forbid();
        if (dto.Role == WatchlistRole.Owner) return BadRequest(new { message = "Owner role cannot be assigned" });
        if (target.Role == WatchlistRole.Admin && actor.Role != WatchlistRole.Owner) return Forbid();

        target.Role = dto.Role;
        target.CanAddItems = dto.Permissions.CanAddItems;
        target.CanRemoveItems = dto.Permissions.CanRemoveItems;
        target.CanReorderItems = dto.Permissions.CanReorderItems;
        target.CanUpdateItemStatus = dto.Permissions.CanUpdateItemStatus;
        target.CanInviteMembers = dto.Permissions.CanInviteMembers;
        target.CanManageMembers = dto.Permissions.CanManageMembers;
        target.CanUpdateWatchlist = dto.Permissions.CanUpdateWatchlist;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:int}/members/{memberId:int}")]
    public async Task<IActionResult> RemoveMember(int id, int memberId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var actor = await GetMemberAsync(id, userId.Value);
        if (actor is null || !EffectivePermissions(actor).CanManageMembers) return Forbid();

        var target = await _context.WatchlistMembers.FirstOrDefaultAsync(m => m.Id == memberId && m.WatchlistId == id);
        if (target is null) return NotFound(new { message = "Member not found" });
        if (target.Role == WatchlistRole.Owner) return Forbid();
        if (target.Role == WatchlistRole.Admin && actor.Role != WatchlistRole.Owner) return Forbid();

        _context.WatchlistMembers.Remove(target);
        var preference = await _context.UserWatchlistPreferences
            .FirstOrDefaultAsync(p => p.UserId == target.UserId && p.DefaultWatchlistId == id);
        if (preference is not null)
            preference.DefaultWatchlistId = null;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("{id:int}/access-requests")]
    public async Task<IActionResult> RequestAccess(int id, [FromBody] CreateWatchlistAccessRequestDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        if (await _context.WatchlistMembers.AnyAsync(m => m.WatchlistId == id && m.UserId == userId.Value))
            return Conflict(new { message = "You already have access" });

        if (!await _context.Watchlists.AnyAsync(w => w.Id == id))
            return NotFound(new { message = "Watchlist not found" });

        var pending = await _context.WatchlistAccessRequests
            .AnyAsync(r => r.WatchlistId == id
                && r.RequestingUserId == userId.Value
                && r.Status == WatchlistAccessRequestStatus.Pending);
        if (pending) return Conflict(new { message = "Access request already pending" });

        _context.WatchlistAccessRequests.Add(new WatchlistAccessRequest
        {
            WatchlistId = id,
            RequestingUserId = userId.Value,
            Message = dto.Message
        });
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("access-requests/{requestId:int}/approve")]
    public async Task<IActionResult> ApproveAccessRequest(int requestId)
    {
        return await ResolveAccessRequestAsync(requestId, true);
    }

    [HttpPost("access-requests/{requestId:int}/reject")]
    public async Task<IActionResult> RejectAccessRequest(int requestId)
    {
        return await ResolveAccessRequestAsync(requestId, false);
    }

    [HttpPut("me/default")]
    public async Task<IActionResult> SetDefaultWatchlist([FromBody] SetDefaultWatchlistDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        if (dto.WatchlistId.HasValue && !await _context.WatchlistMembers.AnyAsync(m => m.UserId == userId.Value && m.WatchlistId == dto.WatchlistId.Value))
            return Forbid();

        var preference = await _context.UserWatchlistPreferences.FirstOrDefaultAsync(p => p.UserId == userId.Value);
        if (preference is null)
        {
            preference = new UserWatchlistPreference { UserId = userId.Value };
            _context.UserWatchlistPreferences.Add(preference);
        }

        preference.DefaultWatchlistId = dto.WatchlistId;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task<ActionResult?> AddItemInternalAsync(int watchlistId, AddWatchlistItemDto dto, int userId)
    {
        if (!await _context.Watchlists.AnyAsync(w => w.Id == watchlistId))
            return NotFound(new { message = "Watchlist not found" });

        if (dto.ItemType == WatchlistItemType.MediaItem)
        {
            if (!dto.MediaItemId.HasValue)
                return BadRequest(new { message = "MediaItemId is required" });

            if (!await _context.MediaItems.AnyAsync(m => m.Id == dto.MediaItemId.Value))
                return NotFound(new { message = "Media item not found" });

            if (await _context.WatchlistItems.AnyAsync(i => i.WatchlistId == watchlistId && i.MediaItemId == dto.MediaItemId.Value))
                return Conflict(new { message = "Media item is already in this watchlist" });
        }
        else
        {
            if (!dto.ChildWatchlistId.HasValue)
                return BadRequest(new { message = "ChildWatchlistId is required" });

            if (dto.ChildWatchlistId.Value == watchlistId)
                return BadRequest(new { message = "A watchlist cannot contain itself" });

            if (!await _context.Watchlists.AnyAsync(w => w.Id == dto.ChildWatchlistId.Value))
                return NotFound(new { message = "Child watchlist not found" });

            if (await _context.WatchlistItems.AnyAsync(i => i.WatchlistId == watchlistId && i.ChildWatchlistId == dto.ChildWatchlistId.Value))
                return Conflict(new { message = "Watchlist is already nested here" });

            if (await WouldCreateCycleAsync(watchlistId, dto.ChildWatchlistId.Value))
                return BadRequest(new { message = "Nested watchlist would create a cycle" });
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

    private async Task<IActionResult> ResolveAccessRequestAsync(int requestId, bool approve)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var request = await _context.WatchlistAccessRequests
            .FirstOrDefaultAsync(r => r.Id == requestId);
        if (request is null) return NotFound(new { message = "Access request not found" });

        var actor = await GetMemberAsync(request.WatchlistId, userId.Value);
        if (actor is null || !EffectivePermissions(actor).CanManageMembers) return Forbid();

        request.Status = approve ? WatchlistAccessRequestStatus.Approved : WatchlistAccessRequestStatus.Rejected;
        request.RespondedAt = DateTime.UtcNow;
        request.RespondedByUserId = userId.Value;

        if (approve && !await _context.WatchlistMembers.AnyAsync(m => m.WatchlistId == request.WatchlistId && m.UserId == request.RequestingUserId))
        {
            var defaults = DefaultsForRole(WatchlistRole.Member);
            _context.WatchlistMembers.Add(new WatchlistMember
            {
                WatchlistId = request.WatchlistId,
                UserId = request.RequestingUserId,
                InvitedByUserId = userId.Value,
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
        return new NoContentResult();
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

        return new WatchlistChildDto
        {
            Id = watchlist.Id,
            Name = watchlist.Name,
            Description = watchlist.Description,
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

        return new WatchlistChildDto
        {
            Id = watchlist.Id,
            Name = watchlist.Name,
            Description = watchlist.Description,
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

        if (profileId.HasValue)
        {
            isInProfile = await _context.ProfileWatchStates
                .AnyAsync(ws => ws.ProfileId == profileId.Value && ws.MediaItemId == mediaItem.Id);
            isBlacklisted = await _context.ProfileMediaBlocks
                .AnyAsync(b => b.ProfileId == profileId.Value && b.MediaItemId == mediaItem.Id);
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

}
