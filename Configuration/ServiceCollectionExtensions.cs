using Microsoft.EntityFrameworkCore;
using Jellywatch.Api.Application.Interfaces;
using Jellywatch.Api.Application.Services;
using Jellywatch.Api.Infrastructure.Persistence;
using Jellywatch.Api.Infrastructure.ExternalServices;
using Jellywatch.Api.Infrastructure.BackgroundJobs;

namespace Jellywatch.Api.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddJellywatchDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var databaseSettings = configuration.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>() ?? new DatabaseSettings();

        var databasePath = databaseSettings.DatabasePath;
        if (!Path.IsPathRooted(databasePath))
        {
            databasePath = Path.GetFullPath(databasePath);
        }

        var connectionString = $"Data Source={databasePath}";

        services.AddDbContext<JellywatchDbContext>(options =>
        {
            options.UseSqlite(connectionString);
            if (databaseSettings.EnableSensitiveDataLogging)
            {
                options.EnableSensitiveDataLogging();
            }
        });

        return services;
    }

    public static IServiceCollection AddJellywatchServices(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IJellyfinApiClient, JellyfinApiClient>();
        services.AddScoped<IStateCalculationService, StateCalculationService>();
        services.AddScoped<IPropagationService, PropagationService>();
        services.AddScoped<ISyncOrchestrationService, SyncOrchestrationService>();
        services.AddHostedService<JellyfinPollingService>();

        services.AddScoped<ITmdbApiClient, TmdbApiClient>();
        services.AddScoped<IOmdbApiClient, OmdbApiClient>();
        services.AddScoped<ITvMazeApiClient, TvMazeApiClient>();
        services.AddScoped<IMetadataResolutionService, MetadataResolutionService>();
        services.AddHostedService<ImportQueueWorker>();

        services.AddScoped<IAssetCacheService, AssetCacheService>();

        services.AddScoped<IWatchStateService, WatchStateService>();

        services.AddScoped<IWatchlistService, WatchlistService>();

        services.AddScoped<IMediaQueryService, MediaQueryService>();
        services.AddScoped<IStatsService, StatsService>();

        services.AddScoped<IDataImportExportService, DataImportExportService>();

        services.AddScoped<IAdminService, AdminService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IBackupScheduleManagementService, BackupScheduleManagementService>();
        services.AddScoped<INoteService, NoteService>();

        services.AddSingleton<Jellywatch.Api.Infrastructure.BackgroundJobs.BackupScheduleService>();
        services.AddHostedService(sp => sp.GetRequiredService<Jellywatch.Api.Infrastructure.BackgroundJobs.BackupScheduleService>());

        return services;
    }

    public static IServiceCollection AddJellywatchHttpClients(this IServiceCollection services)
    {
        services.AddHttpClient("JellyfinClient")
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

        services.AddHttpClient("TmdbClient")
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(15));

        services.AddHttpClient("OmdbClient")
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(10));

        services.AddHttpClient("TvMazeClient")
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(15));

        services.AddHttpClient("AssetClient")
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(30));

        services.AddHttpClient("CoverClient")
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false // Prevent redirect-based SSRF
            });

        services.AddHttpContextAccessor();

        return services;
    }

    public static IServiceCollection AddJellywatchCors(this IServiceCollection services, IConfiguration configuration, ILoggerFactory? loggerFactory = null)
    {
        var corsSettings = configuration.GetSection(CorsSettings.SectionName).Get<CorsSettings>() ?? new CorsSettings();

        if (!corsSettings.AllowedOrigins.Any())
        {
            var logger = loggerFactory?.CreateLogger("Jellywatch.Cors");
            logger?.LogWarning("CORS is configured with AllowAnyOrigin because no specific origins are set. " +
                "Configure CorsSettings:AllowedOrigins or CORS_ALLOWED_ORIGINS for production use.");
        }

        services.AddCors(options =>
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

        return services;
    }

    public static IServiceCollection AddJellywatchAuth(this IServiceCollection services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>() ?? new JwtSettings();

        if (environment.IsProduction())
        {
            var knownDefaults = new[]
            {
                "ThisIsAVerySecureSecretKeyThatShouldBeChangedInProduction123456789",
                "CHANGE_THIS_TO_A_SECURE_KEY_IN_PRODUCTION",
                "YourSecretKeyHere-MustBeAtLeast32Characters!",
                "JellywatchDefaultDevKeyChangeInProduction2025!"
            };

            if (knownDefaults.Any(d => string.Equals(d, jwtSettings.SecretKey, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    "JWT SecretKey is set to a default/insecure value. " +
                    "Set a strong, unique key via the JWT_SECRET_KEY environment variable or JwtSettings:SecretKey configuration before running in Production.");
            }
        }

        services.AddAuthentication(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
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
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<JwtSettings>>();

                        if (context.Exception.Message.Contains("expired") || context.Exception.Message.Contains("IDX10223"))
                        {
                            logger.LogWarning("Token has expired — client needs to re-authenticate");
                        }
                        else
                        {
                            logger.LogError("Authentication failed: {Message}", context.Exception.Message);
                        }
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorization();

        return services;
    }

    public static IServiceCollection AddJellywatchSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(c =>
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

        return services;
    }

    public static void LoadEnvironmentFile(this WebApplicationBuilder builder)
    {
        var envFilePath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
        if (!File.Exists(envFilePath)) return;

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

    public static void ApplyEnvironmentOverrides(this WebApplicationBuilder builder)
    {
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

        var corsOriginsRaw = Environment.GetEnvironmentVariable("CORS_ALLOWED_ORIGINS");
        if (!string.IsNullOrWhiteSpace(corsOriginsRaw))
        {
            var origins = corsOriginsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (var i = 0; i < origins.Length; i++)
            {
                builder.Configuration[$"CorsSettings:AllowedOrigins:{i}"] = origins[i];
            }
        }
    }

    public static void BindConfigurationSections(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JellyfinSettings>(configuration.GetSection(JellyfinSettings.SectionName));
        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.Configure<CorsSettings>(configuration.GetSection(CorsSettings.SectionName));
        services.Configure<DatabaseSettings>(configuration.GetSection(DatabaseSettings.SectionName));
        services.Configure<TmdbSettings>(configuration.GetSection(TmdbSettings.SectionName));
        services.Configure<OmdbSettings>(configuration.GetSection(OmdbSettings.SectionName));
    }
}