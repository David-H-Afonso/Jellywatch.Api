using Microsoft.EntityFrameworkCore.Migrations;
using Jellywatch.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Jellywatch.Api.Migrations;

[DbContext(typeof(JellywatchDbContext))]
[Migration("20260830130000_AddMediaCreditsForFiltering")]
public partial class AddMediaCreditsForFiltering : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "cast_names",
            table: "media_item",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "director_names",
            table: "media_item",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "cast_names", table: "media_item");
        migrationBuilder.DropColumn(name: "director_names", table: "media_item");
    }
}
