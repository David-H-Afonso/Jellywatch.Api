using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;

namespace Jellywatch.Api.Middleware;

public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message, details) = exception switch
        {
            DbUpdateException { InnerException: SqliteException sqliteEx } => HandleSqliteException(sqliteEx),
            DbUpdateException => ((int)HttpStatusCode.BadRequest, "Error saving data", "Verify the data is valid and no duplicates exist"),
            ArgumentException argEx => ((int)HttpStatusCode.BadRequest, "Invalid data", argEx.Message),
            UnauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, "Unauthorized", "You do not have permission to perform this action"),
            KeyNotFoundException => ((int)HttpStatusCode.NotFound, "Resource not found", "The requested item does not exist"),
            _ => ((int)HttpStatusCode.InternalServerError, "Internal server error", "An unexpected error occurred. Please try again")
        };

        context.Response.StatusCode = statusCode;

        var response = new { statusCode, message, details };
        var jsonResponse = JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        await context.Response.WriteAsync(jsonResponse);
    }

    private static (int statusCode, string message, string details) HandleSqliteException(SqliteException sqliteEx)
    {
        return sqliteEx.SqliteErrorCode switch
        {
            19 => ((int)HttpStatusCode.Conflict, "Data conflict", "A record with the same unique value already exists"),
            _ => ((int)HttpStatusCode.BadRequest, "Database error", $"SQLite error: {sqliteEx.Message}")
        };
    }
}
