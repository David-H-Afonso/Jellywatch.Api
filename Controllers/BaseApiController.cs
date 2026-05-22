using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Jellywatch.Api.Application;
using Jellywatch.Api.Common;

namespace Jellywatch.Api.Controllers;

[ApiController]
[Authorize]
public abstract class BaseApiController : ControllerBase
{
    protected int? CurrentUserId => HttpContext.GetUserId();

    protected int GetCurrentUserIdOrDefault(int defaultUserId = 1)
    {
        return HttpContext.GetUserIdOrDefault(defaultUserId);
    }

    protected ActionResult RequireUserId()
    {
        if (!CurrentUserId.HasValue)
        {
            return Unauthorized(new { message = "User authentication required. Please provide a valid JWT token." });
        }
        return Ok();
    }

    protected ActionResult ToActionResult(ServiceResult result)
    {
        if (result.Success)
            return NoContent();

        return result.StatusCode switch
        {
            403 => Forbid(),
            404 => NotFound(new { message = result.Error }),
            409 => Conflict(new { message = result.Error }),
            400 => BadRequest(new { message = result.Error }),
            _ => StatusCode(result.StatusCode ?? 500, new { message = result.Error })
        };
    }

    protected ActionResult ToActionResult<T>(ServiceResult<T> result)
    {
        if (result.Success)
            return Ok(result.Data);

        return result.StatusCode switch
        {
            403 => Forbid(),
            404 => NotFound(new { message = result.Error }),
            409 => Conflict(new { message = result.Error }),
            400 => BadRequest(new { message = result.Error }),
            _ => StatusCode(result.StatusCode ?? 500, new { message = result.Error })
        };
    }
}
