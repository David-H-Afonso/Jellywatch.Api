using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Configuration;
using Jellywatch.Api.Contracts;
using Jellywatch.Api.Domain.Entities;
using Jellywatch.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jellywatch.Api.Application.Services;

public sealed class HouseholdOAuthService : IHouseholdOAuthService
{
    private readonly JellywatchDbContext _context;
    private readonly IHouseholdIntegrationAccess _access;
    private readonly HouseholdIntegrationSettings _settings;

    public HouseholdOAuthService(
        JellywatchDbContext context,
        IHouseholdIntegrationAccess access,
        IOptions<HouseholdIntegrationSettings> settings)
    {
        _context = context;
        _access = access;
        _settings = settings.Value;
    }

    public async Task<HouseholdOperationResult<HouseholdAuthorizeResponse>> AuthorizeAsync(
        int userId,
        HouseholdAuthorizeRequest request)
    {
        var validation = ValidateAuthorizationRequest(request);
        if (validation.Error is not null)
            return HouseholdOperationResult<HouseholdAuthorizeResponse>.Fail(400, validation.Error, validation.Description!);

        if (!request.Approved)
        {
            return HouseholdOperationResult<HouseholdAuthorizeResponse>.Ok(new HouseholdAuthorizeResponse
            {
                RedirectUri = AddRedirectParameters(request.RedirectUri, request.State, error: "access_denied"),
            });
        }

        if (!request.ProfileId.HasValue)
            return HouseholdOperationResult<HouseholdAuthorizeResponse>.Fail(400, "invalid_request", "A profile must be selected.");

        var profile = await _context.Profiles
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.ProfileId.Value && x.UserId == userId);
        var userExists = await _context.Users.AsNoTracking().AnyAsync(x => x.Id == userId);
        if (profile is null || !userExists)
            return HouseholdOperationResult<HouseholdAuthorizeResponse>.Fail(403, "access_denied", "The selected profile is not available to this user.");

        var now = DateTime.UtcNow;
        var code = HouseholdTokenProtector.Create("jwhc_", 32);
        var connection = new HouseholdConnection
        {
            UserId = userId,
            ProfileId = profile.Id,
            ClientId = request.ClientId,
            AccountId = HouseholdTokenProtector.Create("jwp_", 18),
            GrantedScopes = string.Join(' ', validation.Scopes!),
            Status = "active",
            CreatedAt = now,
            UpdatedAt = now,
        };
        var authorizationCode = new HouseholdAuthorizationCode
        {
            ConnectionId = connection.Id,
            CodeHash = HouseholdTokenProtector.Hash(code),
            RedirectUri = request.RedirectUri,
            CodeChallenge = request.CodeChallenge,
            ExpiresAt = now.AddMinutes(Math.Clamp(_settings.AuthorizationCodeMinutes, 1, 10)),
        };

        _context.HouseholdConnections.Add(connection);
        _context.HouseholdAuthorizationCodes.Add(authorizationCode);
        await _context.SaveChangesAsync();

        return HouseholdOperationResult<HouseholdAuthorizeResponse>.Ok(new HouseholdAuthorizeResponse
        {
            RedirectUri = AddRedirectParameters(request.RedirectUri, request.State, code),
        });
    }

    public Task<HouseholdOperationResult<HouseholdTokenResponse>> ExchangeAsync(HouseholdTokenRequest request)
    {
        if (!string.Equals(request.ClientId, _settings.ClientId, StringComparison.Ordinal))
            return Task.FromResult(InvalidGrant("Unknown client."));

        return request.GrantType switch
        {
            "authorization_code" => ExchangeAuthorizationCodeAsync(request),
            "refresh_token" => ExchangeRefreshTokenAsync(request),
            _ => Task.FromResult(HouseholdOperationResult<HouseholdTokenResponse>.Fail(400, "unsupported_grant_type", "The grant type is not supported.")),
        };
    }

    public async Task RevokeAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 256)
            return;

        var tokenHash = HouseholdTokenProtector.Hash(token);
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var connectionId = await _context.HouseholdAccessTokens
            .Where(x => x.TokenHash == tokenHash)
            .Select(x => x.ConnectionId)
            .SingleOrDefaultAsync();
        connectionId ??= await _context.HouseholdRefreshTokens
            .Where(x => x.TokenHash == tokenHash)
            .Select(x => x.ConnectionId)
            .SingleOrDefaultAsync();

        if (connectionId is not null)
        {
            await RevokeConnectionAsync(connectionId, DateTime.UtcNow);
            await _context.SaveChangesAsync();
        }

        await transaction.CommitAsync();
    }

    public async Task<HouseholdOperationResult<HouseholdMeResponse>> GetMeAsync(HttpContext httpContext)
    {
        var access = await _access.AuthorizeAsync(httpContext);
        if (!access.IsAllowed)
            return HouseholdOperationResult<HouseholdMeResponse>.Fail(access.StatusCode, "invalid_token", "The access token is invalid or expired.");

        var displayName = await _context.Profiles
            .Where(x => x.Id == access.ProfileId && x.UserId == access.UserId)
            .Select(x => x.DisplayName)
            .SingleAsync();

        return HouseholdOperationResult<HouseholdMeResponse>.Ok(new HouseholdMeResponse
        {
            ConnectionId = access.ConnectionId!,
            Account = new HouseholdAccountDto { Id = access.AccountId!, DisplayName = displayName },
            Scopes = access.Scopes!.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
        });
    }

    private async Task<HouseholdOperationResult<HouseholdTokenResponse>> ExchangeAuthorizationCodeAsync(HouseholdTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code)
            || request.Code.Length > 256
            || string.IsNullOrWhiteSpace(request.RedirectUri)
            || !HouseholdTokenProtector.IsValidVerifier(request.CodeVerifier))
        {
            return InvalidGrant("The authorization code request is invalid.");
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var codeHash = HouseholdTokenProtector.Hash(request.Code);
        var authorizationCode = await _context.HouseholdAuthorizationCodes
            .Include(x => x.Connection).ThenInclude(x => x.Profile)
            .SingleOrDefaultAsync(x => x.CodeHash == codeHash);
        var now = DateTime.UtcNow;

        if (authorizationCode is null
            || authorizationCode.ConsumedAt.HasValue
            || authorizationCode.ExpiresAt <= now
            || authorizationCode.Connection.Status != "active"
            || authorizationCode.Connection.RevokedAt.HasValue
            || authorizationCode.Connection.Profile.UserId != authorizationCode.Connection.UserId
            || !string.Equals(request.ClientId, authorizationCode.Connection.ClientId, StringComparison.Ordinal)
            || !string.Equals(request.RedirectUri, authorizationCode.RedirectUri, StringComparison.Ordinal)
            || !HouseholdTokenProtector.VerifyS256(request.CodeVerifier!, authorizationCode.CodeChallenge))
        {
            return InvalidGrant("The authorization code is invalid, expired, or already used.");
        }

        authorizationCode.ConsumedAt = now;
        var result = CreateTokenPair(authorizationCode.Connection, authorizationCode.Connection.Profile.DisplayName, Guid.NewGuid().ToString("N"), now);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return HouseholdOperationResult<HouseholdTokenResponse>.Ok(result);
    }

    private async Task<HouseholdOperationResult<HouseholdTokenResponse>> ExchangeRefreshTokenAsync(HouseholdTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) || request.RefreshToken.Length > 256)
            return InvalidGrant("The refresh token is invalid.");

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var tokenHash = HouseholdTokenProtector.Hash(request.RefreshToken);
        var refreshToken = await _context.HouseholdRefreshTokens
            .Include(x => x.Connection).ThenInclude(x => x.Profile)
            .SingleOrDefaultAsync(x => x.TokenHash == tokenHash);
        var now = DateTime.UtcNow;

        if (refreshToken is null)
            return InvalidGrant("The refresh token is invalid.");

        if (refreshToken.RevokedAt.HasValue && refreshToken.ReplacedByTokenId is not null)
        {
            await RevokeFamilyAsync(refreshToken.ConnectionId, refreshToken.FamilyId, now);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return InvalidGrant("Refresh token reuse was detected; this connection has been revoked.");
        }

        if (refreshToken.RevokedAt.HasValue
            || refreshToken.ExpiresAt <= now
            || refreshToken.Connection.Status != "active"
            || refreshToken.Connection.RevokedAt.HasValue
            || refreshToken.Connection.Profile.UserId != refreshToken.Connection.UserId
            || !string.Equals(request.ClientId, refreshToken.Connection.ClientId, StringComparison.Ordinal))
        {
            return InvalidGrant("The refresh token is invalid or expired.");
        }

        refreshToken.RevokedAt = now;
        var result = CreateTokenPair(refreshToken.Connection, refreshToken.Connection.Profile.DisplayName, refreshToken.FamilyId, now);
        refreshToken.ReplacedByTokenId = _context.HouseholdRefreshTokens.Local
            .Where(x => x.ConnectionId == refreshToken.ConnectionId && x.Id != refreshToken.Id)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Id)
            .First();

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        return HouseholdOperationResult<HouseholdTokenResponse>.Ok(result);
    }

    private HouseholdTokenResponse CreateTokenPair(HouseholdConnection connection, string displayName, string familyId, DateTime now)
    {
        var accessMinutes = Math.Clamp(_settings.AccessTokenMinutes, 1, 60);
        var refreshDays = Math.Clamp(_settings.RefreshTokenDays, 1, 90);
        var accessRaw = HouseholdTokenProtector.Create("jwha_", 32);
        var refreshRaw = HouseholdTokenProtector.Create("jwhr_", 48);

        _context.HouseholdAccessTokens.Add(new HouseholdAccessToken
        {
            ConnectionId = connection.Id,
            TokenHash = HouseholdTokenProtector.Hash(accessRaw),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(accessMinutes),
        });
        _context.HouseholdRefreshTokens.Add(new HouseholdRefreshToken
        {
            ConnectionId = connection.Id,
            TokenHash = HouseholdTokenProtector.Hash(refreshRaw),
            FamilyId = familyId,
            CreatedAt = now,
            ExpiresAt = now.AddDays(refreshDays),
        });

        connection.LastUsedAt = now;
        connection.UpdatedAt = now;
        return new HouseholdTokenResponse
        {
            AccessToken = accessRaw,
            ExpiresIn = accessMinutes * 60,
            RefreshToken = refreshRaw,
            RefreshExpiresIn = refreshDays * 86400,
            Scope = connection.GrantedScopes,
            ConnectionId = connection.Id,
            Account = new HouseholdAccountDto { Id = connection.AccountId, DisplayName = displayName },
        };
    }

    private async Task RevokeFamilyAsync(string connectionId, string familyId, DateTime now)
    {
        var family = await _context.HouseholdRefreshTokens
            .Where(x => x.ConnectionId == connectionId && x.FamilyId == familyId && !x.RevokedAt.HasValue)
            .ToListAsync();
        foreach (var token in family) token.RevokedAt = now;
        await RevokeConnectionAsync(connectionId, now);
    }

    private async Task RevokeConnectionAsync(string connectionId, DateTime now)
    {
        var connection = await _context.HouseholdConnections.SingleOrDefaultAsync(x => x.Id == connectionId);
        if (connection is null) return;

        connection.Status = "revoked";
        connection.RevokedAt ??= now;
        connection.UpdatedAt = now;

        var accessTokens = await _context.HouseholdAccessTokens
            .Where(x => x.ConnectionId == connectionId && !x.RevokedAt.HasValue)
            .ToListAsync();
        var refreshTokens = await _context.HouseholdRefreshTokens
            .Where(x => x.ConnectionId == connectionId && !x.RevokedAt.HasValue)
            .ToListAsync();
        foreach (var token in accessTokens) token.RevokedAt = now;
        foreach (var token in refreshTokens) token.RevokedAt = now;
    }

    private (string[]? Scopes, string? Error, string? Description) ValidateAuthorizationRequest(HouseholdAuthorizeRequest request)
    {
        if (!string.Equals(request.ClientId, _settings.ClientId, StringComparison.Ordinal))
            return (null, "unauthorized_client", "Unknown client.");
        if (!_settings.IsRegisteredRedirect(request.RedirectUri))
            return (null, "invalid_request", "The redirect URI is not registered.");
        if (request.State.Length is < 16 or > 512)
            return (null, "invalid_request", "State must be present and unguessable.");
        if (!string.Equals(request.CodeChallengeMethod, "S256", StringComparison.Ordinal)
            || !HouseholdTokenProtector.IsValidCodeChallenge(request.CodeChallenge))
            return (null, "invalid_request", "PKCE S256 is required.");

        var scopes = request.Scopes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        if (scopes.Length == 0 || scopes.Any(x => !HouseholdScopes.Allowed.Contains(x)))
            return (null, "invalid_scope", "One or more requested scopes are not allowed.");

        return (scopes, null, null);
    }

    private static string AddRedirectParameters(string redirectUri, string state, string? code = null, string? error = null)
    {
        var values = new Dictionary<string, string?> { ["state"] = state };
        if (code is not null) values["code"] = code;
        if (error is not null) values["error"] = error;
        return QueryHelpers.AddQueryString(redirectUri, values);
    }

    private static HouseholdOperationResult<HouseholdTokenResponse> InvalidGrant(string description) =>
        HouseholdOperationResult<HouseholdTokenResponse>.Fail(400, "invalid_grant", description);
}
