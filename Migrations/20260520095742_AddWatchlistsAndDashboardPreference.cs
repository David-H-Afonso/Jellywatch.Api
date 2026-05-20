using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Jellywatch.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddWatchlistsAndDashboardPreference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "include_in_dashboard",
                table: "profile_watch_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "exclude_from_dashboard",
                table: "profile_watch_state",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "watchlist",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    owner_user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    state = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_watchlist", x => x.id);
                    table.ForeignKey(
                        name: "FK_watchlist_user_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_watchlist_preference",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    default_watchlist_id = table.Column<int>(type: "INTEGER", nullable: true),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_watchlist_preference", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_watchlist_preference_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_watchlist_preference_watchlist_default_watchlist_id",
                        column: x => x.default_watchlist_id,
                        principalTable: "watchlist",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "watchlist_access_request",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    watchlist_id = table.Column<int>(type: "INTEGER", nullable: false),
                    requesting_user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    responded_by_user_id = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    responded_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_watchlist_access_request", x => x.id);
                    table.ForeignKey(
                        name: "FK_watchlist_access_request_user_requesting_user_id",
                        column: x => x.requesting_user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_watchlist_access_request_user_responded_by_user_id",
                        column: x => x.responded_by_user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_watchlist_access_request_watchlist_watchlist_id",
                        column: x => x.watchlist_id,
                        principalTable: "watchlist",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "watchlist_invitation",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    watchlist_id = table.Column<int>(type: "INTEGER", nullable: false),
                    invited_user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    invited_by_user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    role = table.Column<int>(type: "INTEGER", nullable: false),
                    can_add_items = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    can_remove_items = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    can_reorder_items = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    can_update_item_status = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    can_invite_members = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    can_manage_members = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    can_update_watchlist = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    message = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    responded_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_watchlist_invitation", x => x.id);
                    table.ForeignKey(
                        name: "FK_watchlist_invitation_user_invited_by_user_id",
                        column: x => x.invited_by_user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_watchlist_invitation_user_invited_user_id",
                        column: x => x.invited_user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_watchlist_invitation_watchlist_watchlist_id",
                        column: x => x.watchlist_id,
                        principalTable: "watchlist",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "watchlist_item",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    watchlist_id = table.Column<int>(type: "INTEGER", nullable: false),
                    item_type = table.Column<int>(type: "INTEGER", nullable: false),
                    media_item_id = table.Column<int>(type: "INTEGER", nullable: true),
                    child_watchlist_id = table.Column<int>(type: "INTEGER", nullable: true),
                    status = table.Column<int>(type: "INTEGER", nullable: false),
                    position = table.Column<int>(type: "INTEGER", nullable: false),
                    added_by_user_id = table.Column<int>(type: "INTEGER", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_watchlist_item", x => x.id);
                    table.ForeignKey(
                        name: "FK_watchlist_item_media_item_media_item_id",
                        column: x => x.media_item_id,
                        principalTable: "media_item",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_watchlist_item_user_added_by_user_id",
                        column: x => x.added_by_user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_watchlist_item_watchlist_child_watchlist_id",
                        column: x => x.child_watchlist_id,
                        principalTable: "watchlist",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_watchlist_item_watchlist_watchlist_id",
                        column: x => x.watchlist_id,
                        principalTable: "watchlist",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "watchlist_member",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    watchlist_id = table.Column<int>(type: "INTEGER", nullable: false),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    role = table.Column<int>(type: "INTEGER", nullable: false),
                    invited_by_user_id = table.Column<int>(type: "INTEGER", nullable: true),
                    can_add_items = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    can_remove_items = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    can_reorder_items = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    can_update_item_status = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    can_invite_members = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    can_manage_members = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    can_update_watchlist = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_watchlist_member", x => x.id);
                    table.ForeignKey(
                        name: "FK_watchlist_member_user_invited_by_user_id",
                        column: x => x.invited_by_user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_watchlist_member_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_watchlist_member_watchlist_watchlist_id",
                        column: x => x.watchlist_id,
                        principalTable: "watchlist",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_watchlist_preference_default_watchlist_id",
                table: "user_watchlist_preference",
                column: "default_watchlist_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_watchlist_preference_user_id",
                table: "user_watchlist_preference",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_owner_user_id",
                table: "watchlist",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_access_request_requesting_user_id",
                table: "watchlist_access_request",
                column: "requesting_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_access_request_responded_by_user_id",
                table: "watchlist_access_request",
                column: "responded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_access_request_watchlist_id_requesting_user_id_status",
                table: "watchlist_access_request",
                columns: new[] { "watchlist_id", "requesting_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_invitation_invited_by_user_id",
                table: "watchlist_invitation",
                column: "invited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_invitation_invited_user_id",
                table: "watchlist_invitation",
                column: "invited_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_invitation_watchlist_id_invited_user_id_status",
                table: "watchlist_invitation",
                columns: new[] { "watchlist_id", "invited_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_item_added_by_user_id",
                table: "watchlist_item",
                column: "added_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_item_child_watchlist_id",
                table: "watchlist_item",
                column: "child_watchlist_id");

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_item_media_item_id",
                table: "watchlist_item",
                column: "media_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_item_watchlist_id_child_watchlist_id",
                table: "watchlist_item",
                columns: new[] { "watchlist_id", "child_watchlist_id" },
                unique: true,
                filter: "child_watchlist_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_item_watchlist_id_media_item_id",
                table: "watchlist_item",
                columns: new[] { "watchlist_id", "media_item_id" },
                unique: true,
                filter: "media_item_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_item_watchlist_id_position",
                table: "watchlist_item",
                columns: new[] { "watchlist_id", "position" });

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_member_invited_by_user_id",
                table: "watchlist_member",
                column: "invited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_member_user_id",
                table: "watchlist_member",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_member_watchlist_id_user_id",
                table: "watchlist_member",
                columns: new[] { "watchlist_id", "user_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_watchlist_preference");

            migrationBuilder.DropTable(
                name: "watchlist_access_request");

            migrationBuilder.DropTable(
                name: "watchlist_invitation");

            migrationBuilder.DropTable(
                name: "watchlist_item");

            migrationBuilder.DropTable(
                name: "watchlist_member");

            migrationBuilder.DropTable(
                name: "watchlist");

            migrationBuilder.DropColumn(
                name: "include_in_dashboard",
                table: "profile_watch_state");

            migrationBuilder.DropColumn(
                name: "exclude_from_dashboard",
                table: "profile_watch_state");
        }
    }
}
