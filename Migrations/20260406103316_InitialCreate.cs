using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellywatch.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_queue_item",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    jellyfin_item_id = table.Column<string>(type: "TEXT", nullable: false),
                    media_type = table.Column<int>(type: "INTEGER", nullable: false),
                    priority = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    retry_count = table.Column<int>(type: "INTEGER", nullable: false),
                    next_retry_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_queue_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "media_item",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    media_type = table.Column<int>(type: "INTEGER", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: false),
                    original_title = table.Column<string>(type: "TEXT", nullable: true),
                    overview = table.Column<string>(type: "TEXT", nullable: true),
                    tmdb_id = table.Column<int>(type: "INTEGER", nullable: true),
                    imdb_id = table.Column<string>(type: "TEXT", nullable: true),
                    tvmaze_id = table.Column<int>(type: "INTEGER", nullable: true),
                    poster_path = table.Column<string>(type: "TEXT", nullable: true),
                    backdrop_path = table.Column<string>(type: "TEXT", nullable: true),
                    release_date = table.Column<string>(type: "TEXT", nullable: true),
                    status = table.Column<string>(type: "TEXT", nullable: true),
                    original_language = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_item", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "provider_cache_entry",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    provider = table.Column<int>(type: "INTEGER", nullable: false),
                    external_id = table.Column<string>(type: "TEXT", nullable: false),
                    response_json = table.Column<string>(type: "TEXT", nullable: true),
                    cached_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_cache_entry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    jellyfin_user_id = table.Column<string>(type: "TEXT", nullable: false),
                    username = table.Column<string>(type: "TEXT", nullable: false),
                    is_admin = table.Column<bool>(type: "INTEGER", nullable: false),
                    avatar_url = table.Column<string>(type: "TEXT", nullable: true),
                    JellyfinServerUrl = table.Column<string>(type: "TEXT", nullable: true),
                    preferred_language = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "webhook_event_log",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    raw_payload = table.Column<string>(type: "TEXT", nullable: true),
                    event_type = table.Column<string>(type: "TEXT", nullable: true),
                    received_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    processed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    success = table.Column<bool>(type: "INTEGER", nullable: false),
                    error_message = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_event_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "external_rating",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    media_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    provider = table.Column<int>(type: "INTEGER", nullable: false),
                    score = table.Column<string>(type: "TEXT", nullable: true),
                    vote_count = table.Column<int>(type: "INTEGER", nullable: true),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_rating", x => x.id);
                    table.ForeignKey(
                        name: "FK_external_rating_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "jellyfin_library_item",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    jellyfin_item_id = table.Column<string>(type: "TEXT", nullable: false),
                    jellyfin_parent_id = table.Column<string>(type: "TEXT", nullable: true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    type = table.Column<int>(type: "INTEGER", nullable: true),
                    media_item_id = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jellyfin_library_item", x => x.id);
                    table.ForeignKey(
                        name: "FK_jellyfin_library_item_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "media_translation",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    media_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    language = table.Column<string>(type: "TEXT", nullable: false),
                    title = table.Column<string>(type: "TEXT", nullable: true),
                    overview = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_translation", x => x.id);
                    table.ForeignKey(
                        name: "FK_media_translation_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "metadata_refresh_job",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    media_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    provider = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    last_refreshed = table.Column<DateTime>(type: "TEXT", nullable: true),
                    next_refresh = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_refresh_job", x => x.id);
                    table.ForeignKey(
                        name: "FK_metadata_refresh_job_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "movie",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    media_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    runtime = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movie", x => x.id);
                    table.ForeignKey(
                        name: "FK_movie_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "series",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    media_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    total_seasons = table.Column<int>(type: "INTEGER", nullable: true),
                    total_episodes = table.Column<int>(type: "INTEGER", nullable: true),
                    network = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_series", x => x.id);
                    table.ForeignKey(
                        name: "FK_series_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "profile",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<int>(type: "INTEGER", nullable: true),
                    jellyfin_user_id = table.Column<string>(type: "TEXT", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", nullable: false),
                    is_joint = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile", x => x.id);
                    table.ForeignKey(
                        name: "FK_profile_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "season",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    series_id = table.Column<int>(type: "INTEGER", nullable: false),
                    season_number = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: true),
                    overview = table.Column<string>(type: "TEXT", nullable: true),
                    poster_path = table.Column<string>(type: "TEXT", nullable: true),
                    tmdb_id = table.Column<int>(type: "INTEGER", nullable: true),
                    episode_count = table.Column<int>(type: "INTEGER", nullable: true),
                    air_date = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_season", x => x.id);
                    table.ForeignKey(
                        name: "FK_season_series_series_id",
                        column: x => x.series_id,
                        principalTable: "series",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "propagation_rule",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    source_profile_id = table.Column<int>(type: "INTEGER", nullable: false),
                    target_profile_id = table.Column<int>(type: "INTEGER", nullable: false),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_propagation_rule", x => x.id);
                    table.ForeignKey(
                        name: "FK_propagation_rule_profile_source_profile_id",
                        column: x => x.source_profile_id,
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_propagation_rule_profile_target_profile_id",
                        column: x => x.target_profile_id,
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sync_job",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    type = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    profile_id = table.Column<int>(type: "INTEGER", nullable: true),
                    started_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    completed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    items_processed = table.Column<int>(type: "INTEGER", nullable: false),
                    error_message = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_job", x => x.id);
                    table.ForeignKey(
                        name: "FK_sync_job_profile_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "episode",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    season_id = table.Column<int>(type: "INTEGER", nullable: false),
                    episode_number = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: true),
                    overview = table.Column<string>(type: "TEXT", nullable: true),
                    still_path = table.Column<string>(type: "TEXT", nullable: true),
                    tmdb_id = table.Column<int>(type: "INTEGER", nullable: true),
                    air_date = table.Column<string>(type: "TEXT", nullable: true),
                    runtime = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_episode", x => x.id);
                    table.ForeignKey(
                        name: "FK_episode_season_season_id",
                        column: x => x.season_id,
                        principalTable: "season",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "media_image",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    media_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    season_id = table.Column<int>(type: "INTEGER", nullable: true),
                    episode_id = table.Column<int>(type: "INTEGER", nullable: true),
                    image_type = table.Column<int>(type: "INTEGER", nullable: false),
                    remote_url = table.Column<string>(type: "TEXT", nullable: true),
                    local_path = table.Column<string>(type: "TEXT", nullable: true),
                    width = table.Column<int>(type: "INTEGER", nullable: true),
                    height = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_image", x => x.id);
                    table.ForeignKey(
                        name: "FK_media_image_episode_episode_id",
                        column: x => x.episode_id,
                        principalTable: "episode",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_media_image_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_media_image_season_season_id",
                        column: x => x.season_id,
                        principalTable: "season",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "profile_note",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    profile_id = table.Column<int>(type: "INTEGER", nullable: false),
                    media_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    season_id = table.Column<int>(type: "INTEGER", nullable: true),
                    episode_id = table.Column<int>(type: "INTEGER", nullable: true),
                    text = table.Column<string>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile_note", x => x.id);
                    table.ForeignKey(
                        name: "FK_profile_note_episode_episode_id",
                        column: x => x.episode_id,
                        principalTable: "episode",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_profile_note_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_profile_note_profile_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_profile_note_season_season_id",
                        column: x => x.season_id,
                        principalTable: "season",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "profile_watch_state",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    profile_id = table.Column<int>(type: "INTEGER", nullable: false),
                    media_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    episode_id = table.Column<int>(type: "INTEGER", nullable: true),
                    movie_id = table.Column<int>(type: "INTEGER", nullable: true),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    is_manual_override = table.Column<bool>(type: "INTEGER", nullable: false),
                    last_updated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_profile_watch_state", x => x.id);
                    table.ForeignKey(
                        name: "FK_profile_watch_state_episode_episode_id",
                        column: x => x.episode_id,
                        principalTable: "episode",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_profile_watch_state_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_profile_watch_state_movie_movie_id",
                        column: x => x.movie_id,
                        principalTable: "movie",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_profile_watch_state_profile_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "watch_event",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    profile_id = table.Column<int>(type: "INTEGER", nullable: false),
                    media_item_id = table.Column<int>(type: "INTEGER", nullable: false),
                    episode_id = table.Column<int>(type: "INTEGER", nullable: true),
                    movie_id = table.Column<int>(type: "INTEGER", nullable: true),
                    jellyfin_item_id = table.Column<string>(type: "TEXT", nullable: true),
                    event_type = table.Column<int>(type: "INTEGER", nullable: false),
                    position_ticks = table.Column<long>(type: "INTEGER", nullable: true),
                    source = table.Column<int>(type: "INTEGER", nullable: false),
                    timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_watch_event", x => x.id);
                    table.ForeignKey(
                        name: "FK_watch_event_episode_episode_id",
                        column: x => x.episode_id,
                        principalTable: "episode",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_watch_event_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_watch_event_movie_movie_id",
                        column: x => x.movie_id,
                        principalTable: "movie",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_watch_event_profile_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profile",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_episode_season_id",
                table: "episode",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "IX_external_rating_media_item_id_provider",
                table: "external_rating",
                columns: new[] { "media_item_id", "provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_import_queue_item_jellyfin_item_id",
                table: "import_queue_item",
                column: "jellyfin_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_import_queue_item_status",
                table: "import_queue_item",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_jellyfin_library_item_jellyfin_item_id",
                table: "jellyfin_library_item",
                column: "jellyfin_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_jellyfin_library_item_media_item_id",
                table: "jellyfin_library_item",
                column: "media_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_media_image_episode_id",
                table: "media_image",
                column: "episode_id");

            migrationBuilder.CreateIndex(
                name: "IX_media_image_media_item_id",
                table: "media_image",
                column: "media_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_media_image_season_id",
                table: "media_image",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "IX_media_item_imdb_id",
                table: "media_item",
                column: "imdb_id");

            migrationBuilder.CreateIndex(
                name: "IX_media_item_tmdb_id",
                table: "media_item",
                column: "tmdb_id");

            migrationBuilder.CreateIndex(
                name: "IX_media_item_tvmaze_id",
                table: "media_item",
                column: "tvmaze_id");

            migrationBuilder.CreateIndex(
                name: "IX_media_translation_media_item_id_language",
                table: "media_translation",
                columns: new[] { "media_item_id", "language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_metadata_refresh_job_media_item_id",
                table: "metadata_refresh_job",
                column: "media_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_movie_media_item_id",
                table: "movie",
                column: "media_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_profile_jellyfin_user_id",
                table: "profile",
                column: "jellyfin_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_profile_user_id",
                table: "profile",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_profile_note_episode_id",
                table: "profile_note",
                column: "episode_id");

            migrationBuilder.CreateIndex(
                name: "IX_profile_note_media_item_id",
                table: "profile_note",
                column: "media_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_profile_note_profile_id_media_item_id_season_id_episode_id",
                table: "profile_note",
                columns: new[] { "profile_id", "media_item_id", "season_id", "episode_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_profile_note_season_id",
                table: "profile_note",
                column: "season_id");

            migrationBuilder.CreateIndex(
                name: "IX_profile_watch_state_episode_id",
                table: "profile_watch_state",
                column: "episode_id");

            migrationBuilder.CreateIndex(
                name: "IX_profile_watch_state_media_item_id",
                table: "profile_watch_state",
                column: "media_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_profile_watch_state_movie_id",
                table: "profile_watch_state",
                column: "movie_id");

            migrationBuilder.CreateIndex(
                name: "IX_profile_watch_state_profile_id_media_item_id_episode_id_movie_id",
                table: "profile_watch_state",
                columns: new[] { "profile_id", "media_item_id", "episode_id", "movie_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_propagation_rule_source_profile_id_target_profile_id",
                table: "propagation_rule",
                columns: new[] { "source_profile_id", "target_profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_propagation_rule_target_profile_id",
                table: "propagation_rule",
                column: "target_profile_id");

            migrationBuilder.CreateIndex(
                name: "IX_provider_cache_entry_expires_at",
                table: "provider_cache_entry",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_provider_cache_entry_provider_external_id",
                table: "provider_cache_entry",
                columns: new[] { "provider", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_season_series_id",
                table: "season",
                column: "series_id");

            migrationBuilder.CreateIndex(
                name: "IX_series_media_item_id",
                table: "series",
                column: "media_item_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sync_job_profile_id",
                table: "sync_job",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_jellyfin_user_id",
                table: "user",
                column: "jellyfin_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_username",
                table: "user",
                column: "username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_watch_event_episode_id",
                table: "watch_event",
                column: "episode_id");

            migrationBuilder.CreateIndex(
                name: "IX_watch_event_jellyfin_item_id",
                table: "watch_event",
                column: "jellyfin_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_watch_event_media_item_id",
                table: "watch_event",
                column: "media_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_watch_event_movie_id",
                table: "watch_event",
                column: "movie_id");

            migrationBuilder.CreateIndex(
                name: "IX_watch_event_profile_id",
                table: "watch_event",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "IX_watch_event_timestamp",
                table: "watch_event",
                column: "timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "external_rating");

            migrationBuilder.DropTable(
                name: "import_queue_item");

            migrationBuilder.DropTable(
                name: "jellyfin_library_item");

            migrationBuilder.DropTable(
                name: "media_image");

            migrationBuilder.DropTable(
                name: "media_translation");

            migrationBuilder.DropTable(
                name: "metadata_refresh_job");

            migrationBuilder.DropTable(
                name: "profile_note");

            migrationBuilder.DropTable(
                name: "profile_watch_state");

            migrationBuilder.DropTable(
                name: "propagation_rule");

            migrationBuilder.DropTable(
                name: "provider_cache_entry");

            migrationBuilder.DropTable(
                name: "sync_job");

            migrationBuilder.DropTable(
                name: "watch_event");

            migrationBuilder.DropTable(
                name: "webhook_event_log");

            migrationBuilder.DropTable(
                name: "episode");

            migrationBuilder.DropTable(
                name: "movie");

            migrationBuilder.DropTable(
                name: "profile");

            migrationBuilder.DropTable(
                name: "season");

            migrationBuilder.DropTable(
                name: "user");

            migrationBuilder.DropTable(
                name: "series");

            migrationBuilder.DropTable(
                name: "media_item");
        }
    }
}
