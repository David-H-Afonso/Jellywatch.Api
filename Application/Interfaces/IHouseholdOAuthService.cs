using Jellywatch.Api.Contracts;

namespace Jellywatch.Api.Application.Interfaces;

public interface IHouseholdOAuthService
{
    Task<HouseholdOperationResult<HouseholdAuthorizeResponse>> AuthorizeAsync(int userId, HouseholdAuthorizeRequest request);
    Task<HouseholdOperationResult<HouseholdTokenResponse>> ExchangeAsync(HouseholdTokenRequest request);
    Task RevokeAsync(string? token);
    Task<HouseholdOperationResult<HouseholdMeResponse>> GetMeAsync(HttpContext httpContext);
}

public sealed record HouseholdOperationResult<T>(
    bool Success,
    int StatusCode,
    T? Data = default,
    string? Error = null,
    string? ErrorDescription = null)
{
    public static HouseholdOperationResult<T> Ok(T data) => new(true, StatusCodes.Status200OK, data);
    public static HouseholdOperationResult<T> Fail(int status, string error, string description) => new(false, status, default, error, description);
}
