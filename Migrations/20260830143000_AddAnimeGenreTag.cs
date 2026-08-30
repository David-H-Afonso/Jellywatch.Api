using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Jellywatch.Api.Infrastructure.Persistence;

#nullable disable

namespace Jellywatch.Api.Migrations;

[DbContext(typeof(JellywatchDbContext))]
[Migration("20260830143000_AddAnimeGenreTag")]
public partial class AddAnimeGenreTag : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "media_item"
            SET "genres" = "genres" || ',Anime'
            WHERE "genres" IS NOT NULL
              AND lower("genres") LIKE '%animation%'
              AND lower(COALESCE("original_language", '')) IN ('ja', 'japanese')
              AND lower(',' || replace("genres", ', ', ',') || ',') NOT LIKE '%,anime,%';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE "media_item"
            SET "genres" = trim(replace(replace(replace(',' || "genres" || ',', ',Anime,', ','), ',anime,', ','), ',,', ','), ',')
            WHERE "genres" IS NOT NULL;
            """);
    }
}
