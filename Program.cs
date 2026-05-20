using System.Data.Common;
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

// Backup schedule background service
builder.Services.AddSingleton<Jellywatch.Api.Services.BackupScheduleService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<Jellywatch.Api.Services.BackupScheduleService>());

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
try
{
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<JellywatchDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        await context.Database.OpenConnectionAsync();
        try
        {
            await PrepareDatabaseForMigrationsAsync(context.Database.GetDbConnection(), logger);
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

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .AllowAnonymous();

app.Run();

static async Task PrepareDatabaseForMigrationsAsync(DbConnection connection, ILogger logger)
{
    var hasInitialSchema = await HasInitialSchemaAsync(connection);
    var historyExists = await TableExistsAsync(connection, "__EFMigrationsHistory");

    if (!historyExists && hasInitialSchema)
    {
        logger.LogInformation("Existing Jellywatch schema detected without EF migration history. Creating migration history table.");
        await EnsureMigrationHistoryTableAsync(connection);
        historyExists = true;
    }

    if (!historyExists)
    {
        return;
    }

    if (!hasInitialSchema)
    {
        logger.LogWarning("EF migration history exists, but the base Jellywatch tables are missing. Skipping startup schema repairs and leaving EF Core to apply any pending migrations.");
        return;
    }

    await RecordMigrationIfMissingAsync(connection, "20260409132911_InitialCreate", logger);
    await UpgradePreSquashMigrationHistoryAsync(connection, logger);
    await RepairInitialSchemaDriftAsync(connection, logger);
    await RepairBackupScheduleSchemaAsync(connection, logger);
    await RepairWatchlistSchemaAsync(connection, logger);
}

static async Task<bool> HasInitialSchemaAsync(DbConnection connection)
{
    return await TableExistsAsync(connection, "user")
        && await TableExistsAsync(connection, "media_item")
        && await TableExistsAsync(connection, "profile_watch_state");
}

static async Task UpgradePreSquashMigrationHistoryAsync(DbConnection connection, ILogger logger)
{
    var oldCount = await ExecuteScalarLongAsync(
        connection,
        "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId LIKE '202604%' AND MigrationId != '20260409132911_InitialCreate'");

    if (oldCount == 0)
    {
        return;
    }

    logger.LogInformation("Detected {Count} pre-squash migrations. Upgrading history table...", oldCount);
    await ExecuteNonQueryAsync(
        connection,
        "DELETE FROM __EFMigrationsHistory WHERE MigrationId LIKE '202604%' AND MigrationId != '20260409132911_InitialCreate'");
    await RecordMigrationIfMissingAsync(connection, "20260409132911_InitialCreate", logger);
    logger.LogInformation("Migration history upgraded to squashed InitialCreate.");
}

static async Task RepairInitialSchemaDriftAsync(DbConnection connection, ILogger logger)
{
    await EnsureBlacklistedItemsSchemaAsync(connection, logger);
    await EnsureProfileMediaBlocksSchemaAsync(connection, logger);

    var columns = new (string Table, string Column, string Definition)[]
    {
        ("media_item", "tvdb_id", "INTEGER"),
        ("media_item", "original_title", "TEXT"),
        ("media_item", "imdb_id", "TEXT"),
        ("media_item", "original_language", "TEXT"),
        ("media_item", "genres", "TEXT"),
        ("episode", "air_time", "TEXT"),
        ("episode", "air_time_utc", "TEXT"),
        ("episode", "TmdbRating", "REAL"),
        ("season", "TmdbRating", "REAL"),
        ("watch_event", "CreatedAt", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000'")
    };

    foreach (var (table, column, definition) in columns)
    {
        await AddColumnIfMissingAsync(connection, table, column, definition, logger);
    }

    await RepairProfileWatchStateDataAsync(connection, logger);
}

static async Task RepairBackupScheduleSchemaAsync(DbConnection connection, ILogger logger)
{
    var migrationRecorded = await MigrationRecordedAsync(connection, "20260506182748_AddBackupSchedule");
    var tableExists = await TableExistsAsync(connection, "backup_schedule");

    if (!migrationRecorded && !tableExists)
    {
        return;
    }

    await EnsureBackupScheduleSchemaAsync(connection, logger);
    await RecordMigrationIfMissingAsync(connection, "20260506182748_AddBackupSchedule", logger);
}

static async Task RepairWatchlistSchemaAsync(DbConnection connection, ILogger logger)
{
    var migrationRecorded = await MigrationRecordedAsync(connection, "20260520095742_AddWatchlistsAndDashboardPreference");
    var hasWatchlistArtifact = await TableExistsAsync(connection, "watchlist")
        || await TableExistsAsync(connection, "watchlist_member")
        || await TableExistsAsync(connection, "watchlist_item")
        || await TableExistsAsync(connection, "watchlist_invitation")
        || await TableExistsAsync(connection, "watchlist_access_request")
        || await TableExistsAsync(connection, "user_watchlist_preference")
        || await ColumnExistsAsync(connection, "profile_watch_state", "include_in_dashboard")
        || await ColumnExistsAsync(connection, "profile_watch_state", "exclude_from_dashboard");

    if (!migrationRecorded && !hasWatchlistArtifact)
    {
        return;
    }

    await EnsureWatchlistSchemaAsync(connection, logger);
    await RecordMigrationIfMissingAsync(connection, "20260520095742_AddWatchlistsAndDashboardPreference", logger);
}

static async Task EnsureBlacklistedItemsSchemaAsync(DbConnection connection, ILogger logger)
{
    await ExecuteNonQueryAsync(connection, """
        CREATE TABLE IF NOT EXISTS "BlacklistedItems" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_BlacklistedItems" PRIMARY KEY AUTOINCREMENT,
            "JellyfinItemId" TEXT NOT NULL,
            "DisplayName" TEXT,
            "Reason" TEXT,
            "CreatedAt" TEXT NOT NULL
        )
        """);
    await AddColumnIfMissingAsync(connection, "BlacklistedItems", "JellyfinItemId", "TEXT NOT NULL DEFAULT ''", logger);
    await AddColumnIfMissingAsync(connection, "BlacklistedItems", "DisplayName", "TEXT", logger);
    await AddColumnIfMissingAsync(connection, "BlacklistedItems", "Reason", "TEXT", logger);
    await AddColumnIfMissingAsync(connection, "BlacklistedItems", "CreatedAt", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000'", logger);
}

static async Task EnsureProfileMediaBlocksSchemaAsync(DbConnection connection, ILogger logger)
{
    await ExecuteNonQueryAsync(connection, """
        CREATE TABLE IF NOT EXISTS "ProfileMediaBlocks" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ProfileMediaBlocks" PRIMARY KEY AUTOINCREMENT,
            "ProfileId" INTEGER NOT NULL,
            "MediaItemId" INTEGER NOT NULL,
            "CreatedAt" TEXT NOT NULL,
            CONSTRAINT "FK_ProfileMediaBlocks_media_item_MediaItemId" FOREIGN KEY ("MediaItemId") REFERENCES "media_item" ("id") ON DELETE CASCADE,
            CONSTRAINT "FK_ProfileMediaBlocks_profile_ProfileId" FOREIGN KEY ("ProfileId") REFERENCES "profile" ("id") ON DELETE CASCADE
        )
        """);
    await AddColumnIfMissingAsync(connection, "ProfileMediaBlocks", "ProfileId", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "ProfileMediaBlocks", "MediaItemId", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "ProfileMediaBlocks", "CreatedAt", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000'", logger);
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_ProfileMediaBlocks_ProfileId" ON "ProfileMediaBlocks" ("ProfileId")""");
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_ProfileMediaBlocks_MediaItemId" ON "ProfileMediaBlocks" ("MediaItemId")""");
}

static async Task RepairProfileWatchStateDataAsync(DbConnection connection, ILogger logger)
{
    if (!await TableExistsAsync(connection, "profile_watch_state"))
    {
        return;
    }

    await SetNullsToDefaultAsync(connection, "profile_watch_state", "state", "0", logger);
    await SetNullsToDefaultAsync(connection, "profile_watch_state", "is_manual_override", "0", logger);
    await SetNullsToDefaultAsync(connection, "profile_watch_state", "include_in_dashboard", "0", logger);
    await SetNullsToDefaultAsync(connection, "profile_watch_state", "exclude_from_dashboard", "0", logger);
    await SetNullsToDefaultAsync(connection, "profile_watch_state", "last_updated", "'0001-01-01T00:00:00.0000000'", logger);

    await ExecuteLoggedNonQueryAsync(connection, """
        UPDATE "profile_watch_state"
        SET "media_item_id" = (
            SELECT "series"."media_item_id"
            FROM "episode"
            INNER JOIN "season" ON "episode"."season_id" = "season"."id"
            INNER JOIN "series" ON "season"."series_id" = "series"."id"
            WHERE "episode"."id" = "profile_watch_state"."episode_id"
        )
        WHERE "media_item_id" IS NULL
          AND "episode_id" IS NOT NULL
          AND EXISTS (
              SELECT 1
              FROM "episode"
              INNER JOIN "season" ON "episode"."season_id" = "season"."id"
              INNER JOIN "series" ON "season"."series_id" = "series"."id"
              WHERE "episode"."id" = "profile_watch_state"."episode_id"
                AND "series"."media_item_id" IS NOT NULL
          )
        """, logger, "Backfilled {Count} profile watch state media item id(s) from episode links.");

    await ExecuteLoggedNonQueryAsync(connection, """
        UPDATE "profile_watch_state"
        SET "media_item_id" = (
            SELECT "series"."media_item_id"
            FROM "season"
            INNER JOIN "series" ON "season"."series_id" = "series"."id"
            WHERE "season"."id" = "profile_watch_state"."season_id"
        )
        WHERE "media_item_id" IS NULL
          AND "season_id" IS NOT NULL
          AND EXISTS (
              SELECT 1
              FROM "season"
              INNER JOIN "series" ON "season"."series_id" = "series"."id"
              WHERE "season"."id" = "profile_watch_state"."season_id"
                AND "series"."media_item_id" IS NOT NULL
          )
        """, logger, "Backfilled {Count} profile watch state media item id(s) from season links.");

    await ExecuteLoggedNonQueryAsync(connection, """
        UPDATE "profile_watch_state"
        SET "media_item_id" = (
            SELECT "movie"."media_item_id"
            FROM "movie"
            WHERE "movie"."id" = "profile_watch_state"."movie_id"
        )
        WHERE "media_item_id" IS NULL
          AND "movie_id" IS NOT NULL
          AND EXISTS (
              SELECT 1
              FROM "movie"
              WHERE "movie"."id" = "profile_watch_state"."movie_id"
                AND "movie"."media_item_id" IS NOT NULL
          )
        """, logger, "Backfilled {Count} profile watch state media item id(s) from movie links.");

    await ExecuteLoggedNonQueryAsync(connection, """
        DELETE FROM "profile_watch_state"
        WHERE "profile_id" IS NULL
           OR "media_item_id" IS NULL
           OR NOT EXISTS (
               SELECT 1
               FROM "profile"
               WHERE "profile"."id" = "profile_watch_state"."profile_id"
           )
           OR NOT EXISTS (
               SELECT 1
               FROM "media_item"
               WHERE "media_item"."id" = "profile_watch_state"."media_item_id"
           )
           OR (
               "episode_id" IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM "episode"
                   WHERE "episode"."id" = "profile_watch_state"."episode_id"
               )
           )
           OR (
               "season_id" IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM "season"
                   WHERE "season"."id" = "profile_watch_state"."season_id"
               )
           )
           OR (
               "movie_id" IS NOT NULL
               AND NOT EXISTS (
                   SELECT 1
                   FROM "movie"
                   WHERE "movie"."id" = "profile_watch_state"."movie_id"
               )
           )
        """, logger, "Deleted {Count} invalid profile watch state row(s) with missing required references.");

    await ExecuteLoggedNonQueryAsync(connection, """
        DELETE FROM "profile_watch_state"
        WHERE "id" NOT IN (
            SELECT MAX("id")
            FROM "profile_watch_state"
            GROUP BY
                "profile_id",
                "media_item_id",
                COALESCE("episode_id", -1),
                COALESCE("season_id", -1),
                COALESCE("movie_id", -1)
        )
        """, logger, "Deleted {Count} duplicate profile watch state row(s).");
}

static async Task EnsureBackupScheduleSchemaAsync(DbConnection connection, ILogger logger)
{
    await ExecuteNonQueryAsync(connection, """
        CREATE TABLE IF NOT EXISTS "backup_schedule" (
            "id" INTEGER NOT NULL CONSTRAINT "PK_backup_schedule" PRIMARY KEY AUTOINCREMENT,
            "user_id" INTEGER NOT NULL,
            "is_enabled" INTEGER NOT NULL DEFAULT 0,
            "backup_hour" INTEGER NOT NULL DEFAULT 3,
            "backup_minute" INTEGER NOT NULL DEFAULT 0,
            "destination_path" TEXT NOT NULL DEFAULT '/backups',
            "file_name_prefix" TEXT NOT NULL DEFAULT '',
            "file_name_suffix" TEXT NOT NULL DEFAULT '',
            "retention_count" INTEGER NOT NULL DEFAULT 7,
            "last_run_at" TEXT,
            "last_run_status" TEXT NOT NULL DEFAULT 'never',
            "last_run_message" TEXT,
            CONSTRAINT "FK_backup_schedule_user_user_id" FOREIGN KEY ("user_id") REFERENCES "user" ("id") ON DELETE CASCADE
        )
        """);

    var columns = new (string Column, string Definition)[]
    {
        ("user_id", "INTEGER NOT NULL DEFAULT 0"),
        ("is_enabled", "INTEGER NOT NULL DEFAULT 0"),
        ("backup_hour", "INTEGER NOT NULL DEFAULT 3"),
        ("backup_minute", "INTEGER NOT NULL DEFAULT 0"),
        ("destination_path", "TEXT NOT NULL DEFAULT '/backups'"),
        ("file_name_prefix", "TEXT NOT NULL DEFAULT ''"),
        ("file_name_suffix", "TEXT NOT NULL DEFAULT ''"),
        ("retention_count", "INTEGER NOT NULL DEFAULT 7"),
        ("last_run_at", "TEXT"),
        ("last_run_status", "TEXT NOT NULL DEFAULT 'never'"),
        ("last_run_message", "TEXT")
    };

    foreach (var (column, definition) in columns)
    {
        await AddColumnIfMissingAsync(connection, "backup_schedule", column, definition, logger);
    }

    await SetNullsToDefaultAsync(connection, "backup_schedule", "is_enabled", "0", logger);
    await SetNullsToDefaultAsync(connection, "backup_schedule", "backup_hour", "3", logger);
    await SetNullsToDefaultAsync(connection, "backup_schedule", "backup_minute", "0", logger);
    await SetNullsToDefaultAsync(connection, "backup_schedule", "destination_path", "'/backups'", logger);
    await SetNullsToDefaultAsync(connection, "backup_schedule", "file_name_prefix", "''", logger);
    await SetNullsToDefaultAsync(connection, "backup_schedule", "file_name_suffix", "''", logger);
    await SetNullsToDefaultAsync(connection, "backup_schedule", "retention_count", "7", logger);
    await SetNullsToDefaultAsync(connection, "backup_schedule", "last_run_status", "'never'", logger);

    await ExecuteNonQueryAsync(connection, """CREATE UNIQUE INDEX IF NOT EXISTS "IX_backup_schedule_user_id" ON "backup_schedule" ("user_id")""");
}

static async Task EnsureWatchlistSchemaAsync(DbConnection connection, ILogger logger)
{
    await AddColumnIfMissingAsync(connection, "profile_watch_state", "include_in_dashboard", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "profile_watch_state", "exclude_from_dashboard", "INTEGER NOT NULL DEFAULT 0", logger);
    await SetNullsToDefaultAsync(connection, "profile_watch_state", "include_in_dashboard", "0", logger);
    await SetNullsToDefaultAsync(connection, "profile_watch_state", "exclude_from_dashboard", "0", logger);

    await ExecuteNonQueryAsync(connection, """
        CREATE TABLE IF NOT EXISTS "watchlist" (
            "id" INTEGER NOT NULL CONSTRAINT "PK_watchlist" PRIMARY KEY AUTOINCREMENT,
            "name" TEXT NOT NULL,
            "description" TEXT,
            "owner_user_id" INTEGER NOT NULL,
            "state" INTEGER NOT NULL,
            "created_at" TEXT NOT NULL,
            "updated_at" TEXT NOT NULL,
            CONSTRAINT "FK_watchlist_user_owner_user_id" FOREIGN KEY ("owner_user_id") REFERENCES "user" ("id") ON DELETE CASCADE
        )
        """);
    await AddColumnIfMissingAsync(connection, "watchlist", "name", "TEXT NOT NULL DEFAULT ''", logger);
    await AddColumnIfMissingAsync(connection, "watchlist", "description", "TEXT", logger);
    await AddColumnIfMissingAsync(connection, "watchlist", "owner_user_id", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist", "state", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist", "created_at", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000'", logger);
    await AddColumnIfMissingAsync(connection, "watchlist", "updated_at", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000'", logger);

    await ExecuteNonQueryAsync(connection, """
        CREATE TABLE IF NOT EXISTS "user_watchlist_preference" (
            "id" INTEGER NOT NULL CONSTRAINT "PK_user_watchlist_preference" PRIMARY KEY AUTOINCREMENT,
            "user_id" INTEGER NOT NULL,
            "default_watchlist_id" INTEGER,
            "updated_at" TEXT NOT NULL,
            CONSTRAINT "FK_user_watchlist_preference_user_user_id" FOREIGN KEY ("user_id") REFERENCES "user" ("id") ON DELETE CASCADE,
            CONSTRAINT "FK_user_watchlist_preference_watchlist_default_watchlist_id" FOREIGN KEY ("default_watchlist_id") REFERENCES "watchlist" ("id") ON DELETE SET NULL
        )
        """);
    await AddColumnIfMissingAsync(connection, "user_watchlist_preference", "user_id", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "user_watchlist_preference", "default_watchlist_id", "INTEGER", logger);
    await AddColumnIfMissingAsync(connection, "user_watchlist_preference", "updated_at", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000'", logger);

    await ExecuteNonQueryAsync(connection, """
        CREATE TABLE IF NOT EXISTS "watchlist_access_request" (
            "id" INTEGER NOT NULL CONSTRAINT "PK_watchlist_access_request" PRIMARY KEY AUTOINCREMENT,
            "watchlist_id" INTEGER NOT NULL,
            "requesting_user_id" INTEGER NOT NULL,
            "status" INTEGER NOT NULL,
            "message" TEXT,
            "responded_by_user_id" INTEGER,
            "created_at" TEXT NOT NULL,
            "responded_at" TEXT,
            CONSTRAINT "FK_watchlist_access_request_user_requesting_user_id" FOREIGN KEY ("requesting_user_id") REFERENCES "user" ("id") ON DELETE CASCADE,
            CONSTRAINT "FK_watchlist_access_request_user_responded_by_user_id" FOREIGN KEY ("responded_by_user_id") REFERENCES "user" ("id") ON DELETE SET NULL,
            CONSTRAINT "FK_watchlist_access_request_watchlist_watchlist_id" FOREIGN KEY ("watchlist_id") REFERENCES "watchlist" ("id") ON DELETE CASCADE
        )
        """);
    await AddColumnIfMissingAsync(connection, "watchlist_access_request", "watchlist_id", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_access_request", "requesting_user_id", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_access_request", "status", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_access_request", "message", "TEXT", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_access_request", "responded_by_user_id", "INTEGER", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_access_request", "created_at", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000'", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_access_request", "responded_at", "TEXT", logger);

    await ExecuteNonQueryAsync(connection, """
        CREATE TABLE IF NOT EXISTS "watchlist_invitation" (
            "id" INTEGER NOT NULL CONSTRAINT "PK_watchlist_invitation" PRIMARY KEY AUTOINCREMENT,
            "watchlist_id" INTEGER NOT NULL,
            "invited_user_id" INTEGER NOT NULL,
            "invited_by_user_id" INTEGER NOT NULL,
            "status" INTEGER NOT NULL,
            "role" INTEGER NOT NULL,
            "can_add_items" INTEGER NOT NULL DEFAULT 1,
            "can_remove_items" INTEGER NOT NULL DEFAULT 0,
            "can_reorder_items" INTEGER NOT NULL DEFAULT 1,
            "can_update_item_status" INTEGER NOT NULL DEFAULT 1,
            "can_invite_members" INTEGER NOT NULL DEFAULT 0,
            "can_manage_members" INTEGER NOT NULL DEFAULT 0,
            "can_update_watchlist" INTEGER NOT NULL DEFAULT 0,
            "message" TEXT,
            "created_at" TEXT NOT NULL,
            "responded_at" TEXT,
            CONSTRAINT "FK_watchlist_invitation_user_invited_by_user_id" FOREIGN KEY ("invited_by_user_id") REFERENCES "user" ("id") ON DELETE CASCADE,
            CONSTRAINT "FK_watchlist_invitation_user_invited_user_id" FOREIGN KEY ("invited_user_id") REFERENCES "user" ("id") ON DELETE CASCADE,
            CONSTRAINT "FK_watchlist_invitation_watchlist_watchlist_id" FOREIGN KEY ("watchlist_id") REFERENCES "watchlist" ("id") ON DELETE CASCADE
        )
        """);
    await AddColumnIfMissingAsync(connection, "watchlist_invitation", "watchlist_id", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_invitation", "invited_user_id", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_invitation", "invited_by_user_id", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_invitation", "status", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_invitation", "role", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_invitation", "can_add_items", "INTEGER NOT NULL DEFAULT 1", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_invitation", "can_remove_items", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_invitation", "can_reorder_items", "INTEGER NOT NULL DEFAULT 1", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_invitation", "can_update_item_status", "INTEGER NOT NULL DEFAULT 1", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_invitation", "can_invite_members", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_invitation", "can_manage_members", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_invitation", "can_update_watchlist", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_invitation", "message", "TEXT", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_invitation", "created_at", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000'", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_invitation", "responded_at", "TEXT", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_invitation", "status", "0", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_invitation", "role", "0", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_invitation", "can_add_items", "1", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_invitation", "can_remove_items", "0", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_invitation", "can_reorder_items", "1", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_invitation", "can_update_item_status", "1", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_invitation", "can_invite_members", "0", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_invitation", "can_manage_members", "0", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_invitation", "can_update_watchlist", "0", logger);

    await ExecuteNonQueryAsync(connection, """
        CREATE TABLE IF NOT EXISTS "watchlist_item" (
            "id" INTEGER NOT NULL CONSTRAINT "PK_watchlist_item" PRIMARY KEY AUTOINCREMENT,
            "watchlist_id" INTEGER NOT NULL,
            "item_type" INTEGER NOT NULL,
            "media_item_id" INTEGER,
            "child_watchlist_id" INTEGER,
            "status" INTEGER NOT NULL,
            "position" INTEGER NOT NULL,
            "added_by_user_id" INTEGER,
            "created_at" TEXT NOT NULL,
            "updated_at" TEXT NOT NULL,
            CONSTRAINT "FK_watchlist_item_media_item_media_item_id" FOREIGN KEY ("media_item_id") REFERENCES "media_item" ("id") ON DELETE CASCADE,
            CONSTRAINT "FK_watchlist_item_user_added_by_user_id" FOREIGN KEY ("added_by_user_id") REFERENCES "user" ("id") ON DELETE SET NULL,
            CONSTRAINT "FK_watchlist_item_watchlist_child_watchlist_id" FOREIGN KEY ("child_watchlist_id") REFERENCES "watchlist" ("id") ON DELETE RESTRICT,
            CONSTRAINT "FK_watchlist_item_watchlist_watchlist_id" FOREIGN KEY ("watchlist_id") REFERENCES "watchlist" ("id") ON DELETE CASCADE
        )
        """);
    await AddColumnIfMissingAsync(connection, "watchlist_item", "watchlist_id", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_item", "item_type", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_item", "media_item_id", "INTEGER", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_item", "child_watchlist_id", "INTEGER", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_item", "status", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_item", "position", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_item", "added_by_user_id", "INTEGER", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_item", "created_at", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000'", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_item", "updated_at", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000'", logger);

    await ExecuteNonQueryAsync(connection, """
        CREATE TABLE IF NOT EXISTS "watchlist_member" (
            "id" INTEGER NOT NULL CONSTRAINT "PK_watchlist_member" PRIMARY KEY AUTOINCREMENT,
            "watchlist_id" INTEGER NOT NULL,
            "user_id" INTEGER NOT NULL,
            "role" INTEGER NOT NULL,
            "invited_by_user_id" INTEGER,
            "can_add_items" INTEGER NOT NULL DEFAULT 1,
            "can_remove_items" INTEGER NOT NULL DEFAULT 0,
            "can_reorder_items" INTEGER NOT NULL DEFAULT 1,
            "can_update_item_status" INTEGER NOT NULL DEFAULT 1,
            "can_invite_members" INTEGER NOT NULL DEFAULT 0,
            "can_manage_members" INTEGER NOT NULL DEFAULT 0,
            "can_update_watchlist" INTEGER NOT NULL DEFAULT 0,
            "created_at" TEXT NOT NULL,
            "updated_at" TEXT NOT NULL,
            CONSTRAINT "FK_watchlist_member_user_invited_by_user_id" FOREIGN KEY ("invited_by_user_id") REFERENCES "user" ("id") ON DELETE SET NULL,
            CONSTRAINT "FK_watchlist_member_user_user_id" FOREIGN KEY ("user_id") REFERENCES "user" ("id") ON DELETE CASCADE,
            CONSTRAINT "FK_watchlist_member_watchlist_watchlist_id" FOREIGN KEY ("watchlist_id") REFERENCES "watchlist" ("id") ON DELETE CASCADE
        )
        """);
    await AddColumnIfMissingAsync(connection, "watchlist_member", "watchlist_id", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_member", "user_id", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_member", "role", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_member", "invited_by_user_id", "INTEGER", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_member", "can_add_items", "INTEGER NOT NULL DEFAULT 1", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_member", "can_remove_items", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_member", "can_reorder_items", "INTEGER NOT NULL DEFAULT 1", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_member", "can_update_item_status", "INTEGER NOT NULL DEFAULT 1", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_member", "can_invite_members", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_member", "can_manage_members", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_member", "can_update_watchlist", "INTEGER NOT NULL DEFAULT 0", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_member", "created_at", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000'", logger);
    await AddColumnIfMissingAsync(connection, "watchlist_member", "updated_at", "TEXT NOT NULL DEFAULT '0001-01-01T00:00:00.0000000'", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_member", "role", "0", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_member", "can_add_items", "1", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_member", "can_remove_items", "0", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_member", "can_reorder_items", "1", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_member", "can_update_item_status", "1", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_member", "can_invite_members", "0", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_member", "can_manage_members", "0", logger);
    await SetNullsToDefaultAsync(connection, "watchlist_member", "can_update_watchlist", "0", logger);

    await EnsureWatchlistIndexesAsync(connection);
}

static async Task EnsureWatchlistIndexesAsync(DbConnection connection)
{
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_user_watchlist_preference_default_watchlist_id" ON "user_watchlist_preference" ("default_watchlist_id")""");
    await ExecuteNonQueryAsync(connection, """CREATE UNIQUE INDEX IF NOT EXISTS "IX_user_watchlist_preference_user_id" ON "user_watchlist_preference" ("user_id")""");
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_watchlist_owner_user_id" ON "watchlist" ("owner_user_id")""");
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_watchlist_access_request_requesting_user_id" ON "watchlist_access_request" ("requesting_user_id")""");
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_watchlist_access_request_responded_by_user_id" ON "watchlist_access_request" ("responded_by_user_id")""");
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_watchlist_access_request_watchlist_id_requesting_user_id_status" ON "watchlist_access_request" ("watchlist_id", "requesting_user_id", "status")""");
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_watchlist_invitation_invited_by_user_id" ON "watchlist_invitation" ("invited_by_user_id")""");
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_watchlist_invitation_invited_user_id" ON "watchlist_invitation" ("invited_user_id")""");
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_watchlist_invitation_watchlist_id_invited_user_id_status" ON "watchlist_invitation" ("watchlist_id", "invited_user_id", "status")""");
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_watchlist_item_added_by_user_id" ON "watchlist_item" ("added_by_user_id")""");
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_watchlist_item_child_watchlist_id" ON "watchlist_item" ("child_watchlist_id")""");
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_watchlist_item_media_item_id" ON "watchlist_item" ("media_item_id")""");
    await ExecuteNonQueryAsync(connection, """CREATE UNIQUE INDEX IF NOT EXISTS "IX_watchlist_item_watchlist_id_child_watchlist_id" ON "watchlist_item" ("watchlist_id", "child_watchlist_id") WHERE child_watchlist_id IS NOT NULL""");
    await ExecuteNonQueryAsync(connection, """CREATE UNIQUE INDEX IF NOT EXISTS "IX_watchlist_item_watchlist_id_media_item_id" ON "watchlist_item" ("watchlist_id", "media_item_id") WHERE media_item_id IS NOT NULL""");
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_watchlist_item_watchlist_id_position" ON "watchlist_item" ("watchlist_id", "position")""");
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_watchlist_member_invited_by_user_id" ON "watchlist_member" ("invited_by_user_id")""");
    await ExecuteNonQueryAsync(connection, """CREATE INDEX IF NOT EXISTS "IX_watchlist_member_user_id" ON "watchlist_member" ("user_id")""");
    await ExecuteNonQueryAsync(connection, """CREATE UNIQUE INDEX IF NOT EXISTS "IX_watchlist_member_watchlist_id_user_id" ON "watchlist_member" ("watchlist_id", "user_id")""");
}

static async Task EnsureMigrationHistoryTableAsync(DbConnection connection)
{
    await ExecuteNonQueryAsync(connection, """
        CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
            "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
            "ProductVersion" TEXT NOT NULL
        )
        """);
}

static async Task RecordMigrationIfMissingAsync(DbConnection connection, string migrationId, ILogger logger)
{
    await EnsureMigrationHistoryTableAsync(connection);

    if (await MigrationRecordedAsync(connection, migrationId))
    {
        return;
    }

    await using var command = connection.CreateCommand();
    command.CommandText = """
        INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
        VALUES ($migrationId, '9.0.0')
        """;
    var parameter = command.CreateParameter();
    parameter.ParameterName = "$migrationId";
    parameter.Value = migrationId;
    command.Parameters.Add(parameter);
    await command.ExecuteNonQueryAsync();
    logger.LogInformation("Recorded migration '{Migration}' after verifying/repairing its schema.", migrationId);
}

static async Task<bool> MigrationRecordedAsync(DbConnection connection, string migrationId)
{
    if (!await TableExistsAsync(connection, "__EFMigrationsHistory"))
    {
        return false;
    }

    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\" WHERE \"MigrationId\" = $migrationId";
    var parameter = command.CreateParameter();
    parameter.ParameterName = "$migrationId";
    parameter.Value = migrationId;
    command.Parameters.Add(parameter);
    return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
}

static async Task<bool> TableExistsAsync(DbConnection connection, string tableName)
{
    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tableName";
    var parameter = command.CreateParameter();
    parameter.ParameterName = "$tableName";
    parameter.Value = tableName;
    command.Parameters.Add(parameter);
    return Convert.ToInt64(await command.ExecuteScalarAsync()) > 0;
}

static async Task<bool> ColumnExistsAsync(DbConnection connection, string tableName, string columnName)
{
    if (!await TableExistsAsync(connection, tableName))
    {
        return false;
    }

    await using var command = connection.CreateCommand();
    command.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)})";
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        if (string.Equals(reader["name"]?.ToString(), columnName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
    }

    return false;
}

static async Task AddColumnIfMissingAsync(DbConnection connection, string tableName, string columnName, string columnDefinition, ILogger logger)
{
    if (!await TableExistsAsync(connection, tableName) || await ColumnExistsAsync(connection, tableName, columnName))
    {
        return;
    }

    await ExecuteNonQueryAsync(
        connection,
        $"ALTER TABLE {QuoteIdentifier(tableName)} ADD COLUMN {QuoteIdentifier(columnName)} {columnDefinition}");
    logger.LogInformation("Added missing column {Table}.{Column}", tableName, columnName);
}

static async Task SetNullsToDefaultAsync(DbConnection connection, string tableName, string columnName, string sqlDefaultValue, ILogger logger)
{
    if (!await TableExistsAsync(connection, tableName) || !await ColumnExistsAsync(connection, tableName, columnName))
    {
        return;
    }

    await using var command = connection.CreateCommand();
    command.CommandText = $"""
        UPDATE {QuoteIdentifier(tableName)}
        SET {QuoteIdentifier(columnName)} = {sqlDefaultValue}
        WHERE {QuoteIdentifier(columnName)} IS NULL
        """;
    var updated = await command.ExecuteNonQueryAsync();
    if (updated > 0)
    {
        logger.LogInformation("Backfilled {Count} NULL value(s) in {Table}.{Column}", updated, tableName, columnName);
    }
}

static async Task<long> ExecuteScalarLongAsync(DbConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    return Convert.ToInt64(await command.ExecuteScalarAsync());
}

static async Task ExecuteNonQueryAsync(DbConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync();
}

static async Task ExecuteLoggedNonQueryAsync(DbConnection connection, string sql, ILogger logger, string messageTemplate)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    var affected = await command.ExecuteNonQueryAsync();
    if (affected > 0)
    {
        logger.LogInformation(messageTemplate, affected);
    }
}

static string QuoteIdentifier(string identifier)
{
    return "\"" + identifier.Replace("\"", "\"\"") + "\"";
}
