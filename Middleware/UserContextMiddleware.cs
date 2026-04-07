using System.Security.Claims;

namespace Jellywatch.Api.Middleware;

public class UserContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserContextMiddleware> _logger;

    public UserContextMiddleware(RequestDelegate next, ILogger<UserContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        int? userId = null;

        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var parsedUserId))
            {
                userId = parsedUserId;
            }
        }

        if (!userId.HasValue && context.Request.Headers.TryGetValue("X-User-Id", out var headerUserId))
        {
            if (int.TryParse(headerUserId.FirstOrDefault(), out var parsedUserId))
            {
                userId = parsedUserId;
            }
        }

        if (userId.HasValue)
        {
            context.Items["UserId"] = userId.Value;
        }

        await _next(context);
    }
}
