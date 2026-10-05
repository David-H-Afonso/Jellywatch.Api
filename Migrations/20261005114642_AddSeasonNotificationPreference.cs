using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellywatch.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSeasonNotificationPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "notify_season_updates",
                table: "user",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "notify_season_updates",
                table: "user");
        }
    }
}
