using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Jellywatch.Api.Application.Services;

public sealed class HouseholdIntegrationAccess : IHouseholdIntegrationAccess
{
    private readonly JellywatchDbContext _context;

    public HouseholdIntegrationAccess(JellywatchDbContext context)
    {
        _context = context;
    }

    public async Task<HouseholdAccessResult> AuthorizeAsync(HttpContext httpContext, params string[] requiredScopes)
    {
        if (!TryReadBearer(httpContext, out var rawToken) || !rawToken.StartsWith("jwha_", StringComparison.Ordinal))
            return HouseholdAccessResult.Unauthorized();

        var now = DateTime.UtcNow;
        var tokenHash = HouseholdTokenProtector.Hash(rawToken);
        var accessToken = await _context.HouseholdAccessTokens
            .Include(x => x.Connection).ThenInclude(x => x.User)
            .Include(x => x.Connection).ThenInclude(x => x.Profile)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash);

        if (accessToken is null
            || accessToken.RevokedAt.HasValue
            || accessToken.ExpiresAt <= now
            || accessToken.Connection.Status != "active"
            || accessToken.Connection.RevokedAt.HasValue
            || accessToken.Connection.Profile.UserId != accessToken.Connection.UserId)
        {
            return HouseholdAccessResult.Unauthorized();
        }

        var scopes = accessToken.Connection.GrantedScopes
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.Ordinal);
        if (requiredScopes.Any(scope => !scopes.Contains(scope)))
            return HouseholdAccessResult.Forbidden();

        accessToken.Connection.LastUsedAt = now;
        accessToken.Connection.UpdatedAt = now;
        await _context.SaveChangesAsync();

        return HouseholdAccessResult.Allowed(
            accessToken.Connection.UserId,
            accessToken.Connection.ProfileId,
            accessToken.Connection.Id,
            accessToken.Connection.AccountId,
            scopes);
    }

    private static bool TryReadBearer(HttpContext context, out string token)
    {
        token = string.Empty;
        if (!context.Request.Headers.TryGetValue("Authorization", out var values) || values.Count != 1)
            return false;

        var header = values[0];
        if (header is null || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        token = header[7..].Trim();
        return token.Length is >= 40 and <= 256 && !token.Contains(' ');
    }
}
