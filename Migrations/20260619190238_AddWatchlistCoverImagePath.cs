using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellywatch.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchlistCoverImagePath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cover_image_path",
                table: "watchlist",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cover_image_path",
                table: "watchlist");
        }
    }
}
