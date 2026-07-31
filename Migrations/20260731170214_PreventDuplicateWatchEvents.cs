using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellywatch.Api.Migrations
{
    /// <inheritdoc />
    public partial class PreventDuplicateWatchEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "watch_event"
                WHERE "id" IN (
                    SELECT "id"
                    FROM (
                        SELECT "id",
                               ROW_NUMBER() OVER (
                                   PARTITION BY "profile_id", "jellyfin_item_id"
                                   ORDER BY "timestamp" DESC, "id" DESC
                               ) AS "row_number"
                        FROM "watch_event"
                        WHERE "event_type" = 1
                    )
                    WHERE "row_number" > 1
                );

                DELETE FROM "watch_event"
                WHERE "id" IN (
                    SELECT "id"
                    FROM (
                        SELECT "id",
                               ROW_NUMBER() OVER (
                                   PARTITION BY "profile_id", "episode_id"
                                   ORDER BY "timestamp" DESC, "id" DESC
                               ) AS "row_number"
                        FROM "watch_event"
                        WHERE "event_type" = 3 AND "episode_id" IS NOT NULL
                    )
                    WHERE "row_number" > 1
                );

                DELETE FROM "watch_event"
                WHERE "id" IN (
                    SELECT "id"
                    FROM (
                        SELECT "id",
                               ROW_NUMBER() OVER (
                                   PARTITION BY "profile_id", "movie_id"
                                   ORDER BY "timestamp" DESC, "id" DESC
                               ) AS "row_number"
                        FROM "watch_event"
                        WHERE "event_type" = 3 AND "movie_id" IS NOT NULL
                    )
                    WHERE "row_number" > 1
                );

                DELETE FROM "webhook_event_log"
                WHERE "id" IN (
                    SELECT "id"
                    FROM (
                        SELECT "id",
                               ROW_NUMBER() OVER (
                                   PARTITION BY "event_type",
                                                json_extract("raw_payload", '$.UserId'),
                                                json_extract("raw_payload", '$.ItemId')
                                   ORDER BY "received_at" DESC, "id" DESC
                               ) AS "row_number"
                        FROM "webhook_event_log"
                        WHERE "success" = 1
                          AND "event_type" IN ('PlaybackProgress', 'UserDataSaved')
                          AND json_valid("raw_payload")
                    )
                    WHERE "row_number" > 1
                );
                """);

            migrationBuilder.DropIndex(
                name: "IX_watch_event_profile_id",
                table: "watch_event");

            migrationBuilder.CreateIndex(
                name: "IX_watch_event_profile_episode_finished",
                table: "watch_event",
                columns: new[] { "profile_id", "episode_id" },
                unique: true,
                filter: "\"event_type\" = 3 AND \"episode_id\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_watch_event_profile_item_progress",
                table: "watch_event",
                columns: new[] { "profile_id", "jellyfin_item_id" },
                unique: true,
                filter: "\"event_type\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_watch_event_profile_movie_finished",
                table: "watch_event",
                columns: new[] { "profile_id", "movie_id" },
                unique: true,
                filter: "\"event_type\" = 3 AND \"movie_id\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_watch_event_profile_episode_finished",
                table: "watch_event");

            migrationBuilder.DropIndex(
                name: "IX_watch_event_profile_item_progress",
                table: "watch_event");

            migrationBuilder.DropIndex(
                name: "IX_watch_event_profile_movie_finished",
                table: "watch_event");

            migrationBuilder.CreateIndex(
                name: "IX_watch_event_profile_id",
                table: "watch_event",
                column: "profile_id");
        }
    }
}
