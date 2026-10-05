using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellywatch.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataDailyCycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "metadata_daily_cycle",
                columns: table => new
                {
                    LocalDate = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    JobId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_daily_cycle", x => x.LocalDate);
                    table.ForeignKey(
                        name: "FK_metadata_daily_cycle_bulk_metadata_job_JobId",
                        column: x => x.JobId,
                        principalTable: "bulk_metadata_job",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_metadata_daily_cycle_JobId",
                table: "metadata_daily_cycle",
                column: "JobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "metadata_daily_cycle");
        }
    }
}
