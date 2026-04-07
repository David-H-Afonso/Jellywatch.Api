using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Infrastructure;
using Jellywatch.Api.Services.Auth;
using Jellywatch.Api.Services.Jellyfin;
using Jellywatch.Api.Services.Sync;
using Jellywatch.Api.Domain;

namespace Jellywatch.Api.Controllers;

[Route("api/[controller]")]
public class AuthController : BaseApiController
{
    private readonly JellywatchDbContext _context;
    private readonly IAuthService _authService;
    private readonly IJellyfinApiClient _jellyfinClient;
    private readonly ISyncOrchestrationService _syncService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(JellywatchDbContext context, IAuthService authService, IJellyfinApiClient jellyfinClient, ISyncOrchestrationService syncService, ILogger<AuthController> logger)
    {
        _context = context;
        _authService = authService;
        _jellyfinClient = jellyfinClient;
        _syncService = syncService;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] JellyfinLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ServerUrl) || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "ServerUrl, Username and Password are required." });
        }

        var jellyfinResult = await _jellyfinClient.AuthenticateAsync(request.ServerUrl, request.Username, request.Password);
        if (jellyfinResult == null)
        {
            return Unauthorized(new { message = "Invalid Jellyfin credentials." });
        }

        var user = await _context.Users
            .Include(u => u.Profiles)
            .FirstOrDefaultAsync(u => u.JellyfinUserId == jellyfinResult.UserId);

        if (user == null)
        {
            user = new User
            {
                JellyfinUserId = jellyfinResult.UserId,
                Username = jellyfinResult.Username,
                IsAdmin = jellyfinResult.IsAdmin,
                AvatarUrl = jellyfinResult.AvatarUrl,
                JellyfinServerUrl = request.ServerUrl.TrimEnd('/')
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            var defaultProfile = new Profile
            {
                UserId = user.Id,
                JellyfinUserId = jellyfinResult.UserId,
                DisplayName = jellyfinResult.Username,
                IsJoint = false
            };
            _context.Profiles.Add(defaultProfile);
            await _context.SaveChangesAsync();

            user = await _context.Users.Include(u => u.Profiles).FirstAsync(u => u.Id == user.Id);
        }
        else
        {
            user.Username = jellyfinResult.Username;
            user.IsAdmin = jellyfinResult.IsAdmin;
            user.AvatarUrl = jellyfinResult.AvatarUrl;
            user.JellyfinServerUrl = request.ServerUrl.TrimEnd('/');
            await _context.SaveChangesAsync();
        }

        var token = _authService.GenerateToken(user);

        _logger.LogInformation("User {Username} (JellyfinId: {JellyfinId}) logged in successfully", user.Username, user.JellyfinUserId);

        // Fire-and-forget sync for all profiles belonging to this user
        var profileIds = await _context.Profiles
            .Where(p => p.UserId == user.Id)
            .Select(p => p.Id)
            .ToListAsync();

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var pid in profileIds)
                    await _syncService.RunFullSyncAsync(pid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Post-login sync failed for user {UserId}", user.Id);
            }
        });

        return Ok(new LoginResponse
        {
            UserId = user.Id,
            Token = token,
            IsAdmin = user.IsAdmin
        });
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserMeResponse>> Me()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var user = await _context.Users
            .Include(u => u.Profiles)
            .FirstOrDefaultAsync(u => u.Id == userId.Value);

        if (user == null) return NotFound(new { message = "User not found." });

        return Ok(new UserMeResponse
        {
            Id = user.Id,
            JellyfinUserId = user.JellyfinUserId,
            Username = user.Username,
            IsAdmin = user.IsAdmin,
            AvatarUrl = user.AvatarUrl,
            PreferredLanguage = user.PreferredLanguage,
            Profiles = user.Profiles.Select(p => new ProfileDto
            {
                Id = p.Id,
                DisplayName = p.DisplayName,
                IsJoint = p.IsJoint,
                JellyfinUserId = p.JellyfinUserId
            }).ToList()
        });
    }

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        // JWT is stateless — client discards the token.
        return Ok(new { message = "Logged out successfully." });
    }
}
