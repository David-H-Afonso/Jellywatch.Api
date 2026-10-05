using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellywatch.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBulkMetadataJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "bulk_metadata_job",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ActiveKey = table.Column<string>(type: "TEXT", nullable: true),
                    MediaItemIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Total = table.Column<int>(type: "INTEGER", nullable: false),
                    Processed = table.Column<int>(type: "INTEGER", nullable: false),
                    Succeeded = table.Column<int>(type: "INTEGER", nullable: false),
                    Failed = table.Column<int>(type: "INTEGER", nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bulk_metadata_job", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_bulk_metadata_job_ActiveKey",
                table: "bulk_metadata_job",
                column: "ActiveKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bulk_metadata_job");
        }
    }
}
