using Microsoft.AspNetCore.Mvc;
using Jellywatch.Api.Application;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class WatchlistsController : BaseApiController
{
    private readonly IWatchlistService _watchlistService;

    public WatchlistsController(IWatchlistService watchlistService)
    {
        _watchlistService = watchlistService;
    }

    [HttpGet]
    public async Task<ActionResult<WatchlistIndexDto>> GetWatchlists([FromQuery] int? profileId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.GetWatchlistsAsync(userId.Value, profileId);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<WatchlistUserOptionDto>>> GetUserOptions([FromQuery] string? search, [FromQuery] int? watchlistId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.GetUserOptionsAsync(userId.Value, search, watchlistId);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<WatchlistDetailDto>> GetWatchlist(int id, [FromQuery] int? profileId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.GetWatchlistAsync(userId.Value, id, profileId);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return Ok(result.Data);
    }

    [HttpPost]
    public async Task<ActionResult<WatchlistDetailDto>> CreateWatchlist([FromBody] CreateWatchlistDto dto, [FromQuery] int? profileId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.CreateWatchlistAsync(userId.Value, dto, profileId);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return CreatedAtAction(nameof(GetWatchlist), new { id = result.Data!.Id }, result.Data);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateWatchlist(int id, [FromBody] UpdateWatchlistDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.UpdateWatchlistAsync(userId.Value, id, dto);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteWatchlist(int id)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.DeleteWatchlistAsync(userId.Value, id);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpDelete("{id:int}/me")]
    public async Task<IActionResult> LeaveWatchlist(int id)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.LeaveWatchlistAsync(userId.Value, id);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return NoContent();
    }

    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> CompleteWatchlist(int id)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.CompleteWatchlistAsync(userId.Value, id);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpPost("{id:int}/items")]
    public async Task<IActionResult> AddItem(int id, [FromBody] AddWatchlistItemDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.AddItemAsync(userId.Value, id, dto);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpPut("{id:int}/items/{itemId:int}")]
    public async Task<IActionResult> UpdateItem(int id, int itemId, [FromBody] UpdateWatchlistItemDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.UpdateItemAsync(userId.Value, id, itemId, dto);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpDelete("{id:int}/items/{itemId:int}")]
    public async Task<IActionResult> DeleteItem(int id, int itemId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.DeleteItemAsync(userId.Value, id, itemId);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpPut("{id:int}/items/reorder")]
    public async Task<IActionResult> ReorderItems(int id, [FromBody] ReorderWatchlistItemsDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.ReorderItemsAsync(userId.Value, id, dto);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpPost("{id:int}/members/invite")]
    public async Task<ActionResult<WatchlistInvitationDto>> InviteMember(int id, [FromBody] InviteWatchlistMemberDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.InviteMemberAsync(userId.Value, id, dto);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return Ok(result.Data);
    }

    [HttpPost("invitations/{invitationId:int}/accept")]
    public async Task<IActionResult> AcceptInvitation(int invitationId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.AcceptInvitationAsync(userId.Value, invitationId);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return NoContent();
    }

    [HttpPost("invitations/{invitationId:int}/reject")]
    public async Task<IActionResult> RejectInvitation(int invitationId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.RejectInvitationAsync(userId.Value, invitationId);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return NoContent();
    }

    [HttpPut("{id:int}/members/{memberId:int}")]
    public async Task<IActionResult> UpdateMember(int id, int memberId, [FromBody] UpdateWatchlistMemberDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.UpdateMemberAsync(userId.Value, id, memberId, dto);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpDelete("{id:int}/members/{memberId:int}")]
    public async Task<IActionResult> RemoveMember(int id, int memberId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.RemoveMemberAsync(userId.Value, id, memberId);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpPost("{id:int}/access-requests")]
    public async Task<IActionResult> RequestAccess(int id, [FromBody] CreateWatchlistAccessRequestDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.RequestAccessAsync(userId.Value, id, dto);
        if (!result.Success) return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        return NoContent();
    }

    [HttpPost("access-requests/{requestId:int}/approve")]
    public async Task<IActionResult> ApproveAccessRequest(int requestId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.ApproveAccessRequestAsync(userId.Value, requestId);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpPost("access-requests/{requestId:int}/reject")]
    public async Task<IActionResult> RejectAccessRequest(int requestId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.RejectAccessRequestAsync(userId.Value, requestId);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }

    [HttpPut("me/default")]
    public async Task<IActionResult> SetDefaultWatchlist([FromBody] SetDefaultWatchlistDto dto)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _watchlistService.SetDefaultWatchlistAsync(userId.Value, dto);
        if (!result.Success)
        {
            if (result.StatusCode == 403) return Forbid();
            return StatusCode(result.StatusCode ?? 400, new { message = result.Error });
        }
        return NoContent();
    }
}
