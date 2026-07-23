namespace Jellywatch.Api.Application.Interfaces;

public interface IHouseholdIntegrationAccess
{
    Task<HouseholdAccessResult> AuthorizeAsync(HttpContext httpContext, params string[] requiredScopes);
}

public sealed record HouseholdAccessResult(
    bool IsAllowed,
    int StatusCode,
    int? UserId = null,
    int? ProfileId = null,
    string? ConnectionId = null,
    string? AccountId = null,
    IReadOnlySet<string>? Scopes = null)
{
    public static HouseholdAccessResult Unauthorized() => new(false, StatusCodes.Status401Unauthorized);
    public static HouseholdAccessResult Forbidden() => new(false, StatusCodes.Status403Forbidden);
    public static HouseholdAccessResult Allowed(
        int userId,
        int profileId,
        string connectionId,
        string accountId,
        IReadOnlySet<string> scopes) =>
        new(true, StatusCodes.Status200OK, userId, profileId, connectionId, accountId, scopes);
}
