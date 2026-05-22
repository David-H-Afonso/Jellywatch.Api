using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Configuration;
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
builder.Services.AddJellywatchSwagger();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Jellywatch API v1");
    c.RoutePrefix = "swagger";
});

app.UseMiddleware<ErrorHandlingMiddleware>();

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
app.UseMiddleware<UserContextMiddleware>();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .AllowAnonymous();

app.Run();