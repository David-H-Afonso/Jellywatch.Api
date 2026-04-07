using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Jellywatch.Api.Helpers;

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
}
