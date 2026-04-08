using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Configuration;
using Jellywatch.Api.Infrastructure;
using Jellywatch.Api.Middleware;
using Jellywatch.Api.Services.Auth;
using Jellywatch.Api.Services.Assets;
using Jellywatch.Api.Services.Jellyfin;
using Jellywatch.Api.Services.Metadata;
using Jellywatch.Api.Services.Sync;

var builder = WebApplication.CreateBuilder(args);

// Load environment variables from .env file if it exists (for local development)
var envFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envFilePath))
{
    foreach (var line in File.ReadAllLines(envFilePath))
    {
        if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            continue;

        var parts = line.Split('=', 2);
        if (parts.Length == 2)
        {
            Environment.SetEnvironmentVariable(parts[0].Trim(), parts[1].Trim());
        }
    }
}

// Override configuration with environment variables
builder.Configuration["JellyfinSettings:BaseUrl"] = Environment.GetEnvironmentVariable("JELLYFIN_BASE_URL")
    ?? builder.Configuration["JellyfinSettings:BaseUrl"];
builder.Configuration["JellyfinSettings:ApiKey"] = Environment.GetEnvironmentVariable("JELLYFIN_API_KEY")
    ?? builder.Configuration["JellyfinSettings:ApiKey"];
builder.Configuration["JellyfinSettings:WebhookSecret"] = Environment.GetEnvironmentVariable("JELLYFIN_WEBHOOK_SECRET")
    ?? builder.Configuration["JellyfinSettings:WebhookSecret"];

builder.Configuration["JwtSettings:SecretKey"] = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
    ?? builder.Configuration["JwtSettings:SecretKey"];
builder.Configuration["JwtSettings:Issuer"] = Environment.GetEnvironmentVariable("JWT_ISSUER")
    ?? builder.Configuration["JwtSettings:Issuer"];
builder.Configuration["JwtSettings:Audience"] = Environment.GetEnvironmentVariable("JWT_AUDIENCE")
    ?? builder.Configuration["JwtSettings:Audience"];
if (int.TryParse(Environment.GetEnvironmentVariable("JWT_EXPIRATION_MINUTES"), out var expMinutes))
{
    builder.Configuration["JwtSettings:ExpirationMinutes"] = expMinutes.ToString();
}

builder.Configuration["TmdbSettings:ApiKey"] = Environment.GetEnvironmentVariable("TMDB_API_KEY")
    ?? builder.Configuration["TmdbSettings:ApiKey"];
builder.Configuration["OmdbSettings:ApiKey"] = Environment.GetEnvironmentVariable("OMDB_API_KEY")
    ?? builder.Configuration["OmdbSettings:ApiKey"];

builder.Configuration["DatabaseSettings:DatabasePath"] = Environment.GetEnvironmentVariable("DATABASE_PATH")
    ?? builder.Configuration["DatabaseSettings:DatabasePath"];

// CORS — parse comma-separated origins from CORS_ALLOWED_ORIGINS env var
var corsOriginsRaw = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS");
if (!string.IsNullOrWhiteSpace(corsOriginsRaw))
{
    var origins = corsOriginsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    for (var i = 0; i < origins.Length; i++)
    {
        builder.Configuration[$"CorsSettings:AllowedOrigins:{i}"] = origins[i];
    }
}

// Bind configuration sections to strongly-typed settings
builder.Services.Configure<JellyfinSettings>(
    builder.Configuration.GetSection(JellyfinSettings.SectionName));
builder.Services.Configure<JwtSettings>(
    builder.Configuration.GetSection(JwtSettings.SectionName));
builder.Services.Configure<CorsSettings>(
    builder.Configuration.GetSection(CorsSettings.SectionName));
builder.Services.Configure<DatabaseSettings>(
    builder.Configuration.GetSection(DatabaseSettings.SectionName));
builder.Services.Configure<TmdbSettings>(
    builder.Configuration.GetSection(TmdbSettings.SectionName));
builder.Services.Configure<OmdbSettings>(
    builder.Configuration.GetSection(OmdbSettings.SectionName));

// Add controllers
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

// Configure Entity Framework with SQLite
var databaseSettings = builder.Configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>() ?? new DatabaseSettings();

var databasePath = databaseSettings.DatabasePath;
if (!Path.IsPathRooted(databasePath))
{
    databasePath = Path.GetFullPath(databasePath);
}

var connectionString = $"Data Source={databasePath}";

builder.Services.AddDbContext<JellywatchDbContext>(options =>
{
    options.UseSqlite(connectionString);
    if (databaseSettings.EnableSensitiveDataLogging)
    {
        options.EnableSensitiveDataLogging();
    }
});

// Register services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJellyfinApiClient, JellyfinApiClient>();
builder.Services.AddScoped<IStateCalculationService, StateCalculationService>();
builder.Services.AddScoped<IPropagationService, PropagationService>();
builder.Services.AddScoped<ISyncOrchestrationService, SyncOrchestrationService>();
builder.Services.AddHostedService<JellyfinPollingService>();

// Metadata enrichment services
builder.Services.AddScoped<ITmdbApiClient, TmdbApiClient>();
builder.Services.AddScoped<IOmdbApiClient, OmdbApiClient>();
builder.Services.AddScoped<ITvMazeApiClient, TvMazeApiClient>();
builder.Services.AddScoped<IMetadataResolutionService, MetadataResolutionService>();
builder.Services.AddHostedService<ImportQueueWorker>();

// Asset caching
builder.Services.AddScoped<IAssetCacheService, AssetCacheService>();

builder.Services.AddHttpClient("JellyfinClient")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

builder.Services.AddHttpClient("TmdbClient")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(15);
    });

builder.Services.AddHttpClient("OmdbClient")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
    });

builder.Services.AddHttpClient("TvMazeClient")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(15);
    });

builder.Services.AddHttpClient("AssetClient")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
    });

builder.Services.AddHttpContextAccessor();

// Configure CORS
var corsSettings = builder.Configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigins", policy =>
    {
        if (corsSettings.AllowedOrigins.Any())
        {
            policy.WithOrigins(corsSettings.AllowedOrigins.ToArray())
                  .AllowAnyHeader()
                  .AllowAnyMethod()
                  .AllowCredentials();
        }
        else
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });

    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Configure JWT authentication
var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();
builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(jwtSettings.SecretKey))
        };

        options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();

                if (context.Exception.Message.Contains("expired") || context.Exception.Message.Contains("IDX10223"))
                {
                    logger.LogWarning("Token has expired — client needs to re-authenticate");
                }
                else
                {
                    logger.LogError("Authentication failed: {Message}", context.Exception.Message);
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                if (builder.Environment.IsDevelopment())
                {
                    var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                    var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                    logger.LogDebug("Token validated for UserId: {UserId}", userId);
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Jellywatch API",
        Version = "v0.1.0",
        Description = "API for tracking Jellyfin watch activity with metadata enrichment"
    });

    c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\"",
        Name = "Authorization",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Swagger in all environments
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Jellywatch API v1");
    c.RoutePrefix = "swagger";
});

app.UseMiddleware<ErrorHandlingMiddleware>();

// Ensure database exists and apply pending migrations
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<JellywatchDbContext>();
    context.Database.Migrate();
}

app.UseCors(builder.Environment.IsDevelopment() ? "AllowAll" : "AllowSpecificOrigins");

app.UseAuthentication();
app.UseMiddleware<UserContextMiddleware>();
app.UseAuthorization();

app.MapControllers();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .AllowAnonymous();

app.Run();
