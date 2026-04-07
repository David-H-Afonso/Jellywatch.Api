using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellywatch.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonIdToWatchState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_profile_watch_state_profile_id_media_item_id_episode_id_movie_id",
                table: "profile_watch_state");

            migrationBuilder.AddColumn<int>(
                name: "season_id",
                table: "profile_watch_state",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_profile_watch_state_profile_id_media_item_id_episode_id_season_id_movie_id",
                table: "profile_watch_state",
                columns: new[] { "profile_id", "media_item_id", "episode_id", "season_id", "movie_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_profile_watch_state_season_id",
                table: "profile_watch_state",
                column: "season_id");

            migrationBuilder.AddForeignKey(
                name: "FK_profile_watch_state_season_season_id",
                table: "profile_watch_state",
                column: "season_id",
                principalTable: "season",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_profile_watch_state_season_season_id",
                table: "profile_watch_state");

            migrationBuilder.DropIndex(
                name: "IX_profile_watch_state_profile_id_media_item_id_episode_id_season_id_movie_id",
                table: "profile_watch_state");

            migrationBuilder.DropIndex(
                name: "IX_profile_watch_state_season_id",
                table: "profile_watch_state");

            migrationBuilder.DropColumn(
                name: "season_id",
                table: "profile_watch_state");

            migrationBuilder.CreateIndex(
                name: "IX_profile_watch_state_profile_id_media_item_id_episode_id_movie_id",
                table: "profile_watch_state",
                columns: new[] { "profile_id", "media_item_id", "episode_id", "movie_id" },
                unique: true);
        }
    }
}
