namespace Jellywatch.Api.Application;

public record ServiceResult(bool Success, string? Error = null, int? StatusCode = null)
{
    public static ServiceResult Ok() => new(true);
    public static ServiceResult Fail(string error, int statusCode) => new(false, error, statusCode);
}

public record ServiceResult<T>(bool Success, T? Data = default, string? Error = null, int? StatusCode = null)
{
    public static ServiceResult<T> Ok(T data) => new(true, data);
    public static ServiceResult<T> Fail(string error, int statusCode) => new(false, default, error, statusCode);
}
