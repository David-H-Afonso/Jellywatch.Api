using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Configuration;
using Jellywatch.Api.Infrastructure.ExternalServices;
using Jellywatch.Api.Infrastructure.Persistence;
using Jellywatch.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.LoadEnvironmentFile();
builder.ApplyEnvironmentOverrides();

builder.Services.BindConfigurationSections(builder.Configuration);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddJellywatchDatabase(builder.Configuration);
builder.Services.AddJellywatchServices();
builder.Services.AddJellywatchHttpClients();
builder.Services.AddJellywatchCors(builder.Configuration, LoggerFactory.Create(b => b.AddConsole()));
builder.Services.AddJellywatchAuth(builder.Configuration, builder.Environment);
builder.Services.AddJellywatchRateLimiting();
builder.Services.AddJellywatchSwagger();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Jellywatch API v1");
    c.RoutePrefix = "swagger";
});

app.UseMiddleware<ErrorHandlingMiddleware>();

// Security headers — prevent MIME-sniffing of uploaded/served files
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});

try
{
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<JellywatchDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        await context.Database.OpenConnectionAsync();
        try
        {
            await DatabaseStartupHelper.PrepareDatabaseForMigrationsAsync(context.Database.GetDbConnection(), logger);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        await context.Database.MigrateAsync();
    }
}
catch (Exception ex)
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Database initialization failed - API will continue running. Check database path and permissions.");
}

app.UseCors(builder.Environment.IsDevelopment() ? "AllowAll" : "AllowSpecificOrigins");

app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<UserContextMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", async (
    JellywatchDbContext context,
    IJellyfinApiClient jellyfinClient,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(8));

    var databaseHealthy = false;
    var jellyfinHealthy = false;

    try
    {
        await context.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS \"Value\" FROM \"__EFMigrationsHistory\"")
            .SingleAsync(timeout.Token);
        databaseHealthy = true;
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "SQLite health check failed");
    }

    try
    {
        jellyfinHealthy = await jellyfinClient.IsAvailableAsync(timeout.Token);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Jellyfin health check failed");
    }

    var healthy = databaseHealthy && jellyfinHealthy;
    return Results.Json(new
    {
        status = healthy ? "healthy" : "unhealthy",
        components = new
        {
            database = databaseHealthy ? "healthy" : "unhealthy",
            jellyfin = jellyfinHealthy ? "healthy" : "unhealthy",
        },
        timestamp = DateTime.UtcNow,
    }, statusCode: healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable);
})
    .AllowAnonymous();

app.Run();
